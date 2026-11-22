using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Orbiq.Services;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "Orbiq";
    private const string LegacyValueName = "MiniHub";

    public static bool IsEnabled()
    {
        if (IsValueEnabled(ValueName)) return true;
        if (!IsValueEnabled(LegacyValueName)) return false;

        // Если в предыдущей версии автозагрузка была включена под именем MiniHub,
        // переносим запись на новый Orbiq.exe/Orbiq.dll при первом запуске новой версии.
        try { SetEnabled(true); } catch { /* Оставляем старую активную запись, если Windows временно не даёт её изменить. */ }
        return true;
    }

    private static bool IsValueEnabled(string valueName)
    {
        try
        {
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            if (runKey?.GetValue(valueName) is not string command || string.IsNullOrWhiteSpace(command))
                return false;

            // Task Manager и Settings не удаляют команду из Run при отключении;
            // состояние хранится отдельно в StartupApproved\Run.
            using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, false);
            if (approvedKey?.GetValue(valueName) is byte[] state && state.Length > 0)
                return (state[0] & 0x01) == 0;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        string command = BuildStartupCommand();
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
            ?? throw new InvalidOperationException("Не удалось открыть раздел автозагрузки Windows.");
        using RegistryKey approvedKey = Registry.CurrentUser.CreateSubKey(StartupApprovedKeyPath, true)
            ?? throw new InvalidOperationException("Не удалось изменить состояние автозагрузки Windows.");

        runKey.SetValue(ValueName, command, RegistryValueKind.String);
        byte[] approval = new byte[12];
        approval[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(approval, 4);
        approvedKey.SetValue(ValueName, approval, RegistryValueKind.Binary);
        using RegistryKey? legacyRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        legacyRunKey?.DeleteValue(LegacyValueName, false);
        using RegistryKey? legacyApprovedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
        legacyApprovedKey?.DeleteValue(LegacyValueName, false);
    }

    public static void RemoveEntry()
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        runKey?.DeleteValue(ValueName, false);
        using RegistryKey? approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
        approvedKey?.DeleteValue(ValueName, false);
        using RegistryKey? legacyRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        legacyRunKey?.DeleteValue(LegacyValueName, false);
        using RegistryKey? legacyApprovedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
        legacyApprovedKey?.DeleteValue(LegacyValueName, false);
    }

    private static string BuildStartupCommand()
    {
        string processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь запуска Orbiq.");
        string processName = Path.GetFileNameWithoutExtension(processPath);
        string command;

        if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
                throw new InvalidOperationException("Не удалось определить DLL приложения. Для автозагрузки собери Orbiq через dotnet publish.");
            command = $"\"{processPath}\" \"{assemblyPath}\"";
        }
        else
        {
            command = $"\"{processPath}\"";
        }

        if (command.Length > 260)
            throw new InvalidOperationException("Путь к Orbiq слишком длинный для стандартной записи автозагрузки Windows. Перемести опубликованную программу в папку с более коротким путём.");
        return command;
    }
}
