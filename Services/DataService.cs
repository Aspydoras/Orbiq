using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using Orbiq.Models;

namespace Orbiq.Services;

/// <summary>
/// Хранит данные MiniHub в выбранной пользователем папке, а путь к ней — отдельно,
/// чтобы смена/удаление папки сохранений не стирала настройки расположения.
/// </summary>
public sealed class DataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _configurationDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniHubApp");
    private readonly string _configurationFile;

    public string DefaultDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniHub");

    public string DefaultBackupDirectory { get; } = ResolveDefaultBackupDirectory();

    private static string ResolveDefaultBackupDirectory()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string legacy = Path.Combine(documents, "MiniHub Backups");
        return Directory.Exists(legacy) ? legacy : Path.Combine(documents, "Orbiq Backups");
    }

    public string DataDirectory { get; private set; }
    public string DataFilePath => Path.Combine(DataDirectory, "data.json");
    public string AssetsDirectory => Path.Combine(DataDirectory, "assets");
    public bool NeedsInitialSetup { get; private set; }
    public string OwnershipMarkerPath => Path.Combine(DataDirectory, ".minihub-folder");

    public DataService()
    {
        _configurationFile = Path.Combine(_configurationDirectory, "storage.json");
        DataDirectory = DefaultDataDirectory;
        LoadStorageConfiguration();
    }

    private void LoadStorageConfiguration()
    {
        try
        {
            if (File.Exists(_configurationFile))
            {
                StorageConfiguration? configuration = JsonSerializer.Deserialize<StorageConfiguration>(
                    File.ReadAllText(_configurationFile), JsonOptions);
                if (!string.IsNullOrWhiteSpace(configuration?.DataDirectory))
                {
                    string configuredDirectory = Path.GetFullPath(configuration.DataDirectory);
                    if (PathsOverlap(configuredDirectory, _configurationDirectory) || PathsOverlap(configuredDirectory, AppContext.BaseDirectory))
                        throw new InvalidDataException("В конфигурации указана недопустимая папка сохранений.");
                    DataDirectory = configuredDirectory;
                    Directory.CreateDirectory(DataDirectory);
                    MarkAsMiniHubFolder();
                    NeedsInitialSetup = false;
                    return;
                }
            }
        }
        catch
        {
            // Повреждённая конфигурация не должна мешать открыть приложение.
        }

        // Совместимость с версиями до V2.4: если старый файл уже существует,
        // автоматически используем прежнюю папку и не показываем мастер первого запуска.
        if (File.Exists(Path.Combine(DefaultDataDirectory, "data.json")))
        {
            DataDirectory = DefaultDataDirectory;
            Directory.CreateDirectory(DataDirectory);
            MarkAsMiniHubFolder();
            NeedsInitialSetup = false;
            SaveStorageConfiguration();
            return;
        }

        DataDirectory = DefaultDataDirectory;
        NeedsInitialSetup = true;
    }

    public void ValidateDataDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Не указана папка для сохранений.", nameof(directory));

        string fullPath = Path.GetFullPath(directory.Trim());
        if (IsFilesystemRoot(fullPath))
            throw new InvalidOperationException("Нельзя использовать корень диска как папку сохранений. Создай для Orbiq отдельную папку.");
        if (PathsOverlap(fullPath, _configurationDirectory) || PathsOverlap(fullPath, AppContext.BaseDirectory))
            throw new InvalidOperationException("Выбери отдельную папку. Папка сохранений не должна совпадать с папкой настроек Orbiq или папкой файлов приложения.");
    }

    public string StoreAssetFile(string sourcePath, string filePrefix)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Исходный файл не найден.", sourcePath);

        string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        string safePrefix = string.Concat(filePrefix.Where(c => char.IsLetterOrDigit(c) || c == '-'));
        if (string.IsNullOrWhiteSpace(safePrefix)) safePrefix = "asset";
        Directory.CreateDirectory(AssetsDirectory);
        string fileName = $"{safePrefix}-{Guid.NewGuid():N}{extension}";
        string destination = Path.Combine(AssetsDirectory, fileName);
        File.Copy(sourcePath, destination, false);
        return Path.Combine("assets", fileName);
    }

    public string? ResolveStoredAssetPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        try
        {
            string assetsRoot = Path.GetFullPath(AssetsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(DataDirectory, relativePath));
            if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch { return null; }
    }

    public void DeleteStoredAsset(string? relativePath)
    {
        string? fullPath = ResolveStoredAssetPath(relativePath);
        if (fullPath is null) return;
        try
        {
            File.Delete(fullPath);
            if (Directory.Exists(AssetsDirectory) && !Directory.EnumerateFileSystemEntries(AssetsDirectory).Any())
                Directory.Delete(AssetsDirectory, false);
        }
        catch { /* Не блокируем работу программы из-за неудачной очистки старого медиафайла. */ }
    }

    public void SetDataDirectory(string directory)
    {
        ValidateDataDirectory(directory);
        string fullPath = Path.GetFullPath(directory.Trim());
        Directory.CreateDirectory(fullPath);
        DataDirectory = fullPath;
        MarkAsMiniHubFolder();
        NeedsInitialSetup = false;
        SaveStorageConfiguration();
    }

    public AppData Load()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            if (!File.Exists(DataFilePath)) return new AppData();

            string json = File.ReadAllText(DataFilePath);
            return JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? new AppData();
        }
        catch
        {
            // Не удаляем повреждённый файл: он остаётся в папке для возможного восстановления.
            return new AppData();
        }
    }

    public void Save(AppData data)
    {
        Directory.CreateDirectory(DataDirectory);
        MarkAsMiniHubFolder();
        WriteDataFile(data);
        TryCreateAutomaticBackup(data);
    }

    public void ValidateBackupDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Укажи папку для резервных копий.", nameof(directory));

        string fullPath = Path.GetFullPath(directory.Trim());
        if (IsFilesystemRoot(fullPath))
            throw new InvalidOperationException("Нельзя использовать корень диска для резервных копий. Выбери отдельную папку.");
        if (PathsOverlap(fullPath, DataDirectory) || PathsOverlap(fullPath, _configurationDirectory) ||
            PathsOverlap(fullPath, AppContext.BaseDirectory))
            throw new InvalidOperationException("Папка резервных копий должна быть отдельной от сохранений, настроек и файлов Orbiq.");

        string[] protectedRoots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (string protectedRoot in protectedRoots)
        {
            if (string.IsNullOrWhiteSpace(protectedRoot)) continue;
            string root = Path.GetFullPath(protectedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase) ||
                root.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Выбери отдельную папку резервных копий внутри пользовательского каталога, а не сам рабочий каталог Windows или пользователя.");
        }
    }

    public string CreateBackup(string backupDirectory, string kind = "Manual")
    {
        ValidateBackupDirectory(backupDirectory);
        string fullDirectory = Path.GetFullPath(backupDirectory.Trim());
        Directory.CreateDirectory(fullDirectory);

        string safeKind = string.Concat(kind.Where(char.IsLetterOrDigit));
        if (string.IsNullOrWhiteSpace(safeKind)) safeKind = "Backup";
        string archivePath = Path.Combine(fullDirectory,
            $"Orbiq-{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safeKind}.zip");

        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            if (File.Exists(DataFilePath))
                archive.CreateEntryFromFile(DataFilePath, "data.json", CompressionLevel.Optimal);

            if (Directory.Exists(AssetsDirectory))
            {
                foreach (string file in Directory.GetFiles(AssetsDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(file);
                    if (!IsAllowedMiniHubAssetFile(fileName)) continue;
                    archive.CreateEntryFromFile(file, "assets/" + fileName, CompressionLevel.Optimal);
                }
            }
        }

        // Ограничиваем число архивов Orbiq и наследуемых архивов MiniHub, не затрагивая другие файлы.
        string[] ownedArchives = Directory.GetFiles(fullDirectory, "Orbiq-*.zip")
            .Concat(Directory.GetFiles(fullDirectory, "MiniHub-*.zip"))
            .Where(IsMiniHubBackupArchive)
            .OrderByDescending(File.GetCreationTimeUtc)
            .ToArray();
        foreach (string oldArchive in ownedArchives.Skip(20))
        {
            try { File.Delete(oldArchive); } catch { /* Не мешаем созданию текущей копии. */ }
        }
        return archivePath;
    }

    public string RestoreBackup(string archivePath, string? backupDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            throw new FileNotFoundException("Архив резервной копии не найден.", archivePath);

        string temporaryDirectory = Path.Combine(DataDirectory, ".restore-" + Guid.NewGuid().ToString("N"));
        string stagedDataFile = Path.Combine(temporaryDirectory, "data.json");
        string? safetyCopyPath = null;
        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > 600)
                    throw new InvalidDataException("В архиве нет файлов или в нём слишком много элементов.");

                long totalExpandedBytes = 0;
                ZipArchiveEntry? dataEntry = null;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || Path.IsPathRooted(name) ||
                        name.Split('/').Any(segment => segment is ".." or "."))
                        throw new InvalidDataException("Архив содержит небезопасный путь к файлу.");

                    if (string.Equals(name, "data.json", StringComparison.OrdinalIgnoreCase))
                    {
                        if (dataEntry is not null) throw new InvalidDataException("В архиве найдено несколько файлов data.json.");
                        dataEntry = entry;
                    }
                    else if (name.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                    {
                        string fileName = name.Substring("assets/".Length);
                        // В резерве допускаются только файлы MiniHub непосредственно в assets/.
                        // Вложенные каталоги, ADS/двоеточия и посторонние файлы запрещены.
                        if (!IsAllowedMiniHubAssetFile(fileName))
                            throw new InvalidDataException("В архиве найден неизвестный или небезопасный файл в папке assets.");
                    }
                    else
                    {
                        throw new InvalidDataException("Архив содержит посторонние файлы. Ожидаются только data.json и медиафайлы Orbiq в assets/.");
                    }

                    totalExpandedBytes += entry.Length;
                    if (entry.Length > 100L * 1024 * 1024 || totalExpandedBytes > 250L * 1024 * 1024)
                        throw new InvalidDataException("Размер распакованных данных превышает безопасный лимит.");
                }

                if (dataEntry is null || dataEntry.Length <= 0)
                    throw new InvalidDataException("В архиве отсутствует data.json.");

                Directory.CreateDirectory(temporaryDirectory);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith('/')) continue;
                    string destination = Path.GetFullPath(Path.Combine(temporaryDirectory,
                        entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    string allowedRoot = Path.GetFullPath(temporaryDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Архив пытается записать файл за пределами временной папки.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using Stream input = entry.Open();
                    using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    input.CopyTo(output);
                }
            }

            string json = File.ReadAllText(stagedDataFile);
            AppData? restored = JsonSerializer.Deserialize<AppData>(json, JsonOptions);
            if (restored is null || restored.Settings is null || restored.Notes is null || restored.Reminders is null)
                throw new InvalidDataException("Файл данных в архиве имеет неверную структуру.");

            string targetBackupDirectory = string.IsNullOrWhiteSpace(backupDirectory) ? DefaultBackupDirectory : backupDirectory;
            try
            {
                if (File.Exists(DataFilePath))
                    safetyCopyPath = CreateBackup(targetBackupDirectory, "BeforeRestore");
            }
            catch
            {
                // Восстановление можно выполнить и без safety-копии, если резервная папка временно недоступна.
            }

            string stagedAssets = Path.Combine(temporaryDirectory, "assets");
            if (Directory.Exists(stagedAssets))
            {
                Directory.CreateDirectory(AssetsDirectory);
                foreach (string stagedAsset in Directory.GetFiles(stagedAssets, "*", SearchOption.TopDirectoryOnly))
                {
                    string destination = Path.Combine(AssetsDirectory, Path.GetFileName(stagedAsset));
                    File.Copy(stagedAsset, destination, true);
                }
            }

            string replacement = DataFilePath + ".restore";
            File.Copy(stagedDataFile, replacement, true);
            if (File.Exists(DataFilePath)) File.Replace(replacement, DataFilePath, null);
            else File.Move(replacement, DataFilePath);

            return safetyCopyPath ?? string.Empty;
        }
        finally
        {
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true); } catch { }
            try { DeleteIfExists(DataFilePath + ".restore"); } catch { }
        }
    }

    private void TryCreateAutomaticBackup(AppData data)
    {
        if (!data.Settings.AutomaticBackupsEnabled) return;
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.Equals(data.Settings.LastAutomaticBackupDate, today, StringComparison.Ordinal)) return;

        try
        {
            string directory = string.IsNullOrWhiteSpace(data.Settings.BackupDirectory)
                ? DefaultBackupDirectory
                : data.Settings.BackupDirectory;
            _ = CreateBackup(directory, "Auto");
            data.Settings.LastAutomaticBackupDate = today;
            WriteDataFile(data);
        }
        catch
        {
            // Основное сохранение уже завершено; сбой резервирования не должен ломать приложение.
        }
    }

    private void WriteDataFile(AppData data)
    {
        string tempPath = DataFilePath + ".tmp";
        string json = JsonSerializer.Serialize(data, JsonOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(DataFilePath))
            File.Replace(tempPath, DataFilePath, null);
        else
            File.Move(tempPath, DataFilePath);
    }

    public void SaveStorageConfiguration()
    {
        Directory.CreateDirectory(_configurationDirectory);
        var configuration = new StorageConfiguration { DataDirectory = DataDirectory };
        string temporaryPath = _configurationFile + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(configuration, JsonOptions));
        if (File.Exists(_configurationFile))
            File.Replace(temporaryPath, _configurationFile, null);
        else
            File.Move(temporaryPath, _configurationFile);
    }

    public void DeleteAllSavedDataFiles(string? backupDirectory = null)
    {
        // Удаляем только файлы, созданные MiniHub, не трогая посторонние файлы
        // в выбранной пользователем папке.
        DeleteIfExists(DataFilePath);
        DeleteIfExists(DataFilePath + ".tmp");
        DeleteIfExists(OwnershipMarkerPath);
        DeleteMiniHubAssetFiles(AssetsDirectory);

        if (!string.IsNullOrWhiteSpace(backupDirectory))
            DeleteMiniHubBackups(backupDirectory);
        else
            DeleteMiniHubBackups(DefaultBackupDirectory);

        if (Directory.Exists(DataDirectory) && !Directory.EnumerateFileSystemEntries(DataDirectory).Any())
            Directory.Delete(DataDirectory, false);

        DeleteIfExists(_configurationFile);
        DeleteIfExists(_configurationFile + ".tmp");
        if (Directory.Exists(_configurationDirectory) && !Directory.EnumerateFileSystemEntries(_configurationDirectory).Any())
            Directory.Delete(_configurationDirectory, false);
    }

    public void DeleteMiniHubFilesFromDirectory(string directory)
    {
        string fullPath = Path.GetFullPath(directory);
        DeleteIfExists(Path.Combine(fullPath, "data.json"));
        DeleteIfExists(Path.Combine(fullPath, "data.json.tmp"));
        DeleteIfExists(Path.Combine(fullPath, ".minihub-folder"));
        DeleteMiniHubAssetFiles(Path.Combine(fullPath, "assets"));
        if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
            Directory.Delete(fullPath, false);
    }

    public static bool IsSafeDirectoryForRecursiveDeletion(string directory)
    {
        try
        {
            string fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (IsFilesystemRoot(fullPath)) return false;

            string[] protectedPaths =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                AppContext.BaseDirectory
            };

            foreach (string protectedPath in protectedPaths)
            {
                if (string.IsNullOrWhiteSpace(protectedPath)) continue;
                string normalized = Path.GetFullPath(protectedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(fullPath, normalized, StringComparison.OrdinalIgnoreCase)) return false;
                string normalizedWithSeparator = normalized + Path.DirectorySeparatorChar;
                if (normalizedWithSeparator.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return File.Exists(Path.Combine(fullPath, ".minihub-folder"));
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteDirectoryContents(string directory)
    {
        if (!IsSafeDirectoryForRecursiveDeletion(directory))
            throw new InvalidOperationException("Для безопасности Orbiq не удалил эту папку целиком. Можно удалить только файлы сохранений Orbiq вручную.");

        Directory.Delete(Path.GetFullPath(directory), true);
    }

    private static bool IsAllowedMiniHubAssetFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(':'))
            return false;
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        bool reminderSound = fileName.StartsWith("reminder-sound-", StringComparison.OrdinalIgnoreCase) &&
                             (extension is ".wav" or ".mp3" or ".wma");
        bool bubbleImage = fileName.StartsWith("bubble-icon-", StringComparison.OrdinalIgnoreCase) &&
                           (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif");
        return reminderSound || bubbleImage;
    }

    private static bool IsMiniHubBackupArchive(string archivePath)
    {
        try
        {
            string[] nameParts = Path.GetFileNameWithoutExtension(archivePath).Split('-');
            if (nameParts.Length != 5 || (nameParts[0] != "MiniHub" && nameParts[0] != "Orbiq") ||
                !DateTime.TryParseExact($"{nameParts[1]}-{nameParts[2]}-{nameParts[3]}",
                    "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out _) ||
                nameParts[4] is not ("Auto" or "Manual" or "BeforeRestore"))
                return false;

            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            if (!archive.Entries.Any(entry => string.Equals(entry.FullName, "data.json", StringComparison.OrdinalIgnoreCase)))
                return false;
            return archive.Entries.All(entry => string.Equals(entry.FullName, "data.json", StringComparison.OrdinalIgnoreCase) ||
                (entry.FullName.StartsWith("assets/", StringComparison.OrdinalIgnoreCase) &&
                 IsAllowedMiniHubAssetFile(entry.FullName.Substring("assets/".Length))));
        }
        catch { return false; }
    }

    private void DeleteMiniHubBackups(string backupDirectory)
    {
        try
        {
            ValidateBackupDirectory(backupDirectory);
            string fullPath = Path.GetFullPath(backupDirectory);
            if (!Directory.Exists(fullPath)) return;
            foreach (string archive in Directory.GetFiles(fullPath, "Orbiq-*.zip")
                         .Concat(Directory.GetFiles(fullPath, "MiniHub-*.zip")))
                if (IsMiniHubBackupArchive(archive)) DeleteIfExists(archive);
            if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
                Directory.Delete(fullPath, false);
        }
        catch { /* Не затрагиваем посторонние данные из-за проблем с очисткой резервов. */ }
    }

    private static void DeleteMiniHubAssetFiles(string assetsDirectory)
    {
        if (!Directory.Exists(assetsDirectory)) return;
        foreach (string file in Directory.GetFiles(assetsDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (IsAllowedMiniHubAssetFile(Path.GetFileName(file)))
                DeleteIfExists(file);
        }
        if (!Directory.EnumerateFileSystemEntries(assetsDirectory).Any())
            Directory.Delete(assetsDirectory, false);
    }

    private void MarkAsMiniHubFolder()
    {
        try
        {
            if (!File.Exists(OwnershipMarkerPath))
            {
                File.WriteAllText(OwnershipMarkerPath, "Папка данных Orbiq (совместимый формат старого MiniHub). Не удаляйте, если хотите сохранить данные.");
                File.SetAttributes(OwnershipMarkerPath, FileAttributes.Hidden);
            }
        }
        catch
        {
            // Само сохранение данных продолжит работать, даже если маркер создать нельзя.
        }
    }


    private static bool PathsOverlap(string first, string second)
    {
        string a = Path.GetFullPath(first).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string b = Path.GetFullPath(second).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFilesystemRoot(string path)
    {
        string root = Path.GetPathRoot(path) ?? string.Empty;
        return string.Equals(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class StorageConfiguration
    {
        public string DataDirectory { get; set; } = string.Empty;
    }
}
