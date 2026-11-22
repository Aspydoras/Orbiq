using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Ellipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using Orbiq.Models;
using Orbiq.Services;

namespace Orbiq;

public partial class MainWindow : Window
{
    private const double MinimumBubbleSize = 44;
    private const double MaximumBubbleSize = 220;
    private const double BubbleWindowPadding = 48;
    private const double BubbleEdgePadding = 12;
    private readonly DataService _dataService = new();
    private AppData _appData = new();
    private Forms.NotifyIcon? _trayIcon;
    private Drawing.Icon? _trayImageIcon;
    private ReminderToastWindow? _toastWindow;
    private MediaPlayer? _customReminderPlayer;
    private readonly DispatcherTimer _bubbleGifTicker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly ScaleTransform _bubbleImageScale = new(1, 1);
    private readonly TranslateTransform _bubbleImageOffset = new();
    private BitmapSource[] _bubbleGifFrames = Array.Empty<BitmapSource>();
    private int[] _bubbleGifFrameDelays = Array.Empty<int>();
    private int _bubbleGifFrameIndex;
    private bool _allowClose;
    private bool _isExpanded;
    private bool _bubbleOnRight = true;
    private string _currentPage = "Clock";
    private Action? _dialogConfirmAction;
    private Action? _dialogCancelAction;
    private Action? _dialogThirdAction;
    private bool _skipSaveOnClose;
    private ThemePalette _palette = ThemePalette.Dark;
    private readonly System.Windows.Media.Effects.DropShadowEffect _bubbleGlow = new()
    {
        Color = Color.FromRgb(100, 216, 203),
        BlurRadius = 16,
        ShadowDepth = 0,
        Opacity = 0
    };

    // Часы
    private readonly DispatcherTimer _clockTicker = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly DispatcherTimer _reminderTicker = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _pomodoroTicker = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _monitorTicker = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly SystemMonitorService _systemMonitor = new();
    private HwndSource? _windowSource;
    private readonly Dictionary<int, string> _registeredHotkeys = new();
    private int _nextHotkeyId = 0x5100;
    private ContextMenu? _quickActionsMenu;

    private static readonly (string Id, string Label)[] HotkeyActionDefinitions =
    {
        ("TogglePanel", "Открыть / свернуть Orbiq"),
        ("QuickActions", "Открыть быстрые действия"),
        ("Clock", "Открыть часы"),
        ("Notes", "Открыть заметки"),
        ("Calculator", "Открыть калькулятор"),
        ("Converter", "Открыть конвертер"),
        ("Reminders", "Открыть напоминания"),
        ("Pomodoro", "Открыть Pomodoro"),
        ("Calendar", "Открыть календарь"),
        ("Monitor", "Открыть мониторинг")
    };
    private readonly Stopwatch _stopwatch = new();
    private readonly Stopwatch _countdownWatch = new();
    private TextBlock? _clockDisplay;
    private TextBlock? _clockCaption;
    private ShapePath? _timerArc;
    private Ellipse? _timerTrack;
    private Border? _timerInputPanel;
    private TextBox? _hoursBox;
    private TextBox? _minutesBox;
    private TextBox? _secondsBox;
    private Button? _clockStartButton;
    private Button? _stopwatchModeButton;
    private Button? _timerModeButton;
    private bool _timerMode;
    private readonly List<string> _stopwatchLaps = new();
    private int _stopwatchLapNumber;
    private TimeSpan _lastStopwatchLapElapsed = TimeSpan.Zero;
    private TextBlock? _stopwatchLapsText;
    private Border? _stopwatchLapsPanel;
    private Button? _stopwatchLapButton;
    private bool _countdownRunning;
    private bool _timerInputDirty;
    private bool _suppressTimerInput;
    private TimeSpan _timerDuration = TimeSpan.Zero;
    private TimeSpan _timerRemaining = TimeSpan.Zero;

    // Калькулятор
    private TextBox? _calculatorDisplay;
    private Button? _degreeButton;
    private readonly List<Button> _trigButtons = new();
    private readonly ExpressionEvaluator _expressionEvaluator = new();
    private TextBlock? _calculatorHistoryText;
    private readonly CurrencyRateService _currencyRateService = new();
    private TextBlock? _currencyStatus;
    private Button? _currencyRefreshButton;
    private StackPanel? _currencyActionPanel;
    private int _currencyRequestId;
    private bool _secondMode;
    private bool _degreeMode = true;

    // Конвертер
    private ComboBox? _categoryCombo;
    private ComboBox? _fromUnitCombo;
    private ComboBox? _toUnitCombo;
    private TextBox? _converterInput;
    private TextBlock? _converterResult;
    private bool _updatingConverter;

    // Напоминания и настройки
    private TextBox? _reminderTextInput;
    private TextBox? _reminderTimeInput;
    private readonly Dictionary<string, TextBox> _paletteInputs = new();
    private readonly List<CheckBox> _reminderDayCheckBoxes = new();
    private ComboBox? _reminderRepeatModeCombo;
    private bool _reminderScheduleMigrationPending;
    private string _noteSearchQuery = string.Empty;

    // Pomodoro
    private string _pomodoroPhase = "Работа";
    private TimeSpan _pomodoroRemaining = TimeSpan.FromMinutes(25);
    private TimeSpan _pomodoroPhaseDuration = TimeSpan.FromMinutes(25);
    private DateTime _pomodoroEndsAt;
    private bool _pomodoroRunning;
    private TextBlock? _pomodoroPhaseText;
    private TextBlock? _pomodoroTimeText;
    private TextBlock? _pomodoroSessionsText;
    private ShapePath? _pomodoroArc;
    private Button? _pomodoroStartButton;
    private TextBox? _pomodoroWorkInput;
    private TextBox? _pomodoroShortBreakInput;
    private TextBox? _pomodoroLongBreakInput;
    private TextBox? _pomodoroCyclesInput;

    // Календарь
    private DateTime _calendarMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calendarSelectedDate = DateTime.Today;

    // Мониторинг системы
    private readonly Dictionary<string, MonitorMetricUi> _monitorMetrics = new();

    public MainWindow()
    {
        InitializeComponent();
        Bubble.Effect = _bubbleGlow;
        SourceInitialized += MainWindow_SourceInitialized;
        _appData = _dataService.NeedsInitialSetup ? new AppData() : _dataService.Load();
        NormalizeAppData();
        LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
        if (_reminderScheduleMigrationPending && !_dataService.NeedsInitialSetup)
        {
            try
            {
                _dataService.Save(_appData);
                _reminderScheduleMigrationPending = false;
            }
            catch
            {
                // Сбой миграции не должен блокировать запуск; данные останутся в памяти
                // и будут сохранены при следующем обычном сохранении.
            }
        }

        ApplyTheme();
        ApplyShellLocalization();
        ApplyWelcomeText();
        _clockTicker.Tick += ClockTicker_Tick;
        _reminderTicker.Tick += ReminderTicker_Tick;
        _pomodoroTicker.Tick += PomodoroTicker_Tick;
        _monitorTicker.Tick += MonitorTicker_Tick;
        InitializeTrayIcon();
        InitializeQuickActionsMenu();
        _bubbleGifTicker.Tick += BubbleGifTicker_Tick;
        InitializeBubbleImageClip();
        ApplyBubbleSize(reposition: false);
        ApplyBubbleAppearance();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        double bubbleWindowSize = GetBubbleWindowSize();
        PlaceBubbleAtEdge(workArea.Top + (workArea.Height - bubbleWindowSize) / 2);
        ShowBubble();
        NavigateTo("Clock");
        RefreshGlobalHotkeys(showErrors: false);

        if (_dataService.NeedsInitialSetup)
        {
            ExpandPanel();
            ShowFirstRunStoragePrompt();
        }
        else
        {
            SynchronizeStartupSetting();
            _reminderTicker.Start();
        }
    }

    private void NormalizeAppData()
    {
        _appData.Notes ??= new List<NoteItem>();
        foreach (NoteItem note in _appData.Notes)
        {
            note.Title ??= "Без названия";
            note.Content ??= string.Empty;
            note.Category ??= string.Empty;
            note.Color ??= "#1B2A40";
        }
        _appData.Reminders ??= new List<ReminderItem>();
        foreach (ReminderItem reminder in _appData.Reminders)
        {
            reminder.Text ??= "Новое напоминание";
            reminder.Time ??= "20:00";
            if (!Regex.IsMatch(reminder.Time, "^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")) reminder.Time = "20:00";
            reminder.LastTriggeredDate ??= string.Empty;
            reminder.RepeatDays ??= new List<int>();
            reminder.RepeatDays = reminder.RepeatDays.Where(day => day is >= 1 and <= 7).Distinct().OrderBy(day => day).ToList();
        }
        _appData.CalendarEvents ??= new List<CalendarEventItem>();
        foreach (CalendarEventItem calendarEvent in _appData.CalendarEvents)
        {
            calendarEvent.Title ??= "Событие";
            calendarEvent.Time ??= "09:00";
            if (!Regex.IsMatch(calendarEvent.Time, "^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")) calendarEvent.Time = "09:00";
            calendarEvent.Details ??= string.Empty;
            calendarEvent.LastTriggeredMinute ??= string.Empty;
        }
        _appData.Settings ??= new AppSettings();
        _appData.Settings.LanguageCode = LocalizationService.NormalizeCode(_appData.Settings.LanguageCode);
        if (_appData.Settings.OneTimeReminderScheduleVersion < 1)
        {
            DateTime migrationNow = DateTime.Now;
            foreach (ReminderItem reminder in _appData.Reminders)
            {
                // Активные напоминания старых версий переводим с выбранной даты
                // на ближайшее будущее наступление заданного времени. Уже сработавшие
                // или отложенные напоминания не переносим повторно.
                if (reminder.IsOneTime && reminder.IsEnabled &&
                    string.IsNullOrWhiteSpace(reminder.LastTriggeredDate) &&
                    !reminder.SnoozeUntil.HasValue)
                {
                    reminder.OneTimeDate = GetNextOneTimeDate(reminder.Time, migrationNow);
                }
                else if (reminder.IsOneTime && !reminder.OneTimeDate.HasValue)
                {
                    reminder.OneTimeDate = GetNextOneTimeDate(reminder.Time, migrationNow);
                }
            }
            _appData.Settings.OneTimeReminderScheduleVersion = 1;
            _reminderScheduleMigrationPending = true;
        }
        _appData.Settings.Theme ??= "Тёмная";
        _appData.Settings.NotificationSound ??= "Мягкий сигнал";
        _appData.Settings.CustomReminderSoundPath ??= string.Empty;
        _appData.Settings.CustomBubbleImagePath ??= string.Empty;
        _appData.Settings.WelcomeText ??= "Приятного использования";
        _appData.Settings.Hotkeys ??= new Dictionary<string, string>();
        _appData.Settings.CalculatorHistory ??= new List<string>();
        _appData.Settings.CalculatorHistory = _appData.Settings.CalculatorHistory.Take(50).ToList();
        if (!double.IsFinite(_appData.Settings.CalculatorMemory)) _appData.Settings.CalculatorMemory = 0;
        _appData.Settings.BackupDirectory ??= string.Empty;
        _appData.Settings.LastAutomaticBackupDate ??= string.Empty;
        _appData.Settings.CustomWindow ??= "#101827";
        _appData.Settings.CustomPanel ??= "#121B2B";
        _appData.Settings.CustomCard ??= "#192438";
        _appData.Settings.CustomButton ??= "#26364D";
        _appData.Settings.CustomAccent ??= "#64D8CB";
        _appData.Settings.CustomText ??= "#F3F6FC";
        _appData.Settings.CustomMuted ??= "#AAB6CA";
        _appData.Settings.CustomBorder ??= "#35445A";
        if (!double.IsFinite(_appData.Settings.NotificationVolume)) _appData.Settings.NotificationVolume = 50;
        _appData.Settings.NotificationVolume = Math.Clamp(_appData.Settings.NotificationVolume, 0, 100);
        _appData.Settings.PomodoroWorkMinutes = Math.Clamp(_appData.Settings.PomodoroWorkMinutes, 1, 180);
        _appData.Settings.PomodoroShortBreakMinutes = Math.Clamp(_appData.Settings.PomodoroShortBreakMinutes, 1, 90);
        _appData.Settings.PomodoroLongBreakMinutes = Math.Clamp(_appData.Settings.PomodoroLongBreakMinutes, 1, 180);
        _appData.Settings.PomodoroCyclesBeforeLongBreak = Math.Clamp(_appData.Settings.PomodoroCyclesBeforeLongBreak, 1, 12);
        _appData.Settings.PomodoroCompletedSessions = Math.Max(0, _appData.Settings.PomodoroCompletedSessions);
        if (string.IsNullOrWhiteSpace(_appData.Settings.WelcomeText))
            _appData.Settings.WelcomeText = "Приятного использования";
        if (!double.IsFinite(_appData.Settings.BubbleSize)) _appData.Settings.BubbleSize = 60;
        _appData.Settings.BubbleSize = Math.Clamp(_appData.Settings.BubbleSize, MinimumBubbleSize, MaximumBubbleSize);
        // Масштаб изображения не имеет фиксированного верхнего предела.
        // Некорректные значения из старого/повреждённого файла сбрасываем безопасно.
        if (!double.IsFinite(_appData.Settings.CustomBubbleZoom) || _appData.Settings.CustomBubbleZoom < 1.0)
            _appData.Settings.CustomBubbleZoom = 1.0;
        if (!double.IsFinite(_appData.Settings.CustomBubbleOffsetX)) _appData.Settings.CustomBubbleOffsetX = 0;
        if (!double.IsFinite(_appData.Settings.CustomBubbleOffsetY)) _appData.Settings.CustomBubbleOffsetY = 0;
        _appData.Settings.CustomBubbleOffsetX = Math.Clamp(_appData.Settings.CustomBubbleOffsetX, -100, 100);
        _appData.Settings.CustomBubbleOffsetY = Math.Clamp(_appData.Settings.CustomBubbleOffsetY, -100, 100);
        _pomodoroPhaseDuration = PomodoroDurationForPhase();
        if (!_pomodoroRunning && _pomodoroPhase == "Работа" && _pomodoroRemaining == TimeSpan.FromMinutes(25))
            _pomodoroRemaining = _pomodoroPhaseDuration;

        var knownHotkeyActions = new HashSet<string>(HotkeyActionDefinitions.Select(item => item.Id), StringComparer.Ordinal);
        var uniqueHotkeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string action in _appData.Settings.Hotkeys.Keys.ToArray())
        {
            if (!knownHotkeyActions.Contains(action))
            {
                _appData.Settings.Hotkeys.Remove(action);
                continue;
            }
            string value = _appData.Settings.Hotkeys[action] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) || !TryParseHotkey(value, out uint modifiers, out uint virtualKey))
            {
                _appData.Settings.Hotkeys.Remove(action);
                continue;
            }
            string signature = $"{modifiers:X8}:{virtualKey:X8}";
            if (!uniqueHotkeys.Add(signature))
                _appData.Settings.Hotkeys.Remove(action);
            else
                _appData.Settings.Hotkeys[action] = FormatHotkeyBinding(modifiers, virtualKey);
        }
    }

    private void ApplyWelcomeText()
    {
        if (WelcomeTextBlock is not null)
        {
            string greeting = string.IsNullOrWhiteSpace(_appData.Settings.WelcomeText)
                ? "Приятного использования"
                : _appData.Settings.WelcomeText.Trim();
            WelcomeTextBlock.Text = string.Equals(greeting, "Приятного использования", StringComparison.Ordinal)
                ? LocalizationService.Translate(greeting) : greeting;
        }
    }

    private void ApplyShellLocalization()
    {
        LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
        Title = "Orbiq";
        WelcomeSubtitleText.Text = LocalizationService.Translate("Orbiq — твоя небольшая многофункциональная панель");
        CollapseButton.ToolTip = LocalizationService.Translate("Свернуть в кружок");
        TrayButton.ToolTip = LocalizationService.Translate("Скрыть в системный трей");
        CloseButton.ToolTip = LocalizationService.Translate("Закрыть приложение");

        ClockNavButton.Content = "◷   " + LocalizationService.Translate("Часы");
        NotesNavButton.Content = "▤   " + LocalizationService.Translate("Заметки");
        CalculatorNavButton.Content = "＋   " + LocalizationService.Translate("Калькулятор");
        ConverterNavButton.Content = "⇄   " + LocalizationService.Translate("Конвертер");
        RemindersNavButton.Content = "◷   " + LocalizationService.Translate("Напоминания");
        PomodoroNavButton.Content = "◉   " + LocalizationService.Translate("Pomodoro");
        CalendarNavButton.Content = "▦   " + LocalizationService.Translate("Календарь");
        MonitorNavButton.Content = "▥   " + LocalizationService.Translate("Мониторинг");
        SettingsNavButton.Content = "⚙   " + LocalizationService.Translate("Настройки");
        BrandFooterText.Text = "ORBIQ  •  " + LocalizationService.Translate("Персональная панель");
        ApplyWelcomeText();

        if (_trayIcon is not null)
        {
            _trayIcon.Text = LocalizationService.Translate("Orbiq — открыть панель");
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(LocalizationService.Translate("Открыть Orbiq"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(LocalizationService.Translate("Выход"), null, (_, _) => Dispatcher.Invoke(RequestClose));
            _trayIcon.ContextMenuStrip = menu;
        }
    }

    private void InitializeBubbleImageClip()
    {
        BubbleContentGrid.SizeChanged += (_, _) => UpdateBubbleClip();
        BubbleImage.Clip = null;
        BubbleImage.RenderTransformOrigin = new Point(0.5, 0.5);
        BubbleImage.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { _bubbleImageScale, _bubbleImageOffset }
        };
        UpdateBubbleClip();
    }

    private double GetMaximumBubbleSize()
    {
        Rect area = SystemParameters.WorkArea;
        // Оставляем по 12 px до края экрана и учитываем рамку окна вокруг кружка.
        double available = Math.Min(area.Width, area.Height) - (BubbleEdgePadding * 2) - BubbleWindowPadding;
        return Math.Max(MinimumBubbleSize, Math.Min(MaximumBubbleSize, available));
    }

    // Исходные фотографии не отклоняются из-за разрешения: декодер уменьшает их
    // до рабочего размера, чтобы не загружать в память гигантский bitmap.
    private const int MaxStillImageDecodeDimension = 8192;
    private const long MaxGifDecodedPixelsTotal = 24_000_000; // общий бюджет готовых кадров (~96 МБ пикселей)
    private const int MaxGifFrames = 120;
    private const long MaxImageFileSize = 100L * 1024 * 1024;

    private long GetMaximumImageFileSize() => MaxImageFileSize;

    private string GetImageLimitDescription() =>
        "Ограничения по разрешению исходного фото нет: большие изображения автоматически уменьшаются при загрузке. " +
        "GIF также автоматически оптимизируется по размеру кадров и их количеству (до 120 кадров). " +
        "Максимальный размер файла — 100 МБ.";

    private double GetBubbleSize()
    {
        double maximum = GetMaximumBubbleSize();
        double size = Math.Clamp(_appData.Settings.BubbleSize, MinimumBubbleSize, maximum);
        _appData.Settings.BubbleSize = size;
        return size;
    }

    private double BubbleSizeToPercent(double size)
    {
        double range = Math.Max(1, GetMaximumBubbleSize() - MinimumBubbleSize);
        return Math.Clamp((size - MinimumBubbleSize) / range * 100.0, 0, 100);
    }

    private double BubblePercentToSize(double percent)
    {
        double maximum = GetMaximumBubbleSize();
        return MinimumBubbleSize + (maximum - MinimumBubbleSize) * Math.Clamp(percent, 0, 100) / 100.0;
    }

    private double GetDefaultBubbleSize() => BubblePercentToSize(50);

    private double GetBubbleWindowSize() => GetBubbleSize() + BubbleWindowPadding;

    private void UpdateBubbleClip()
    {
        double width = BubbleContentGrid.ActualWidth;
        double height = BubbleContentGrid.ActualHeight;
        if (width <= 0 || height <= 0) return;
        BubbleContentGrid.Clip = new EllipseGeometry(new Rect(0, 0, width, height));
    }

    private void ApplyBubbleSize(bool reposition = true)
    {
        double size = GetBubbleSize();
        Bubble.Width = size;
        Bubble.Height = size;
        Bubble.CornerRadius = new CornerRadius(size / 2);
        BubbleImage.Width = size;
        BubbleImage.Height = size;
        BubbleGlyph.Width = size * 0.86;
        BubbleGlyph.Height = size * 0.86;
        double offsetScale = size * 0.0045;
        _bubbleImageOffset.X = _appData.Settings.CustomBubbleOffsetX * offsetScale;
        _bubbleImageOffset.Y = _appData.Settings.CustomBubbleOffsetY * offsetScale;
        UpdateBubbleClip();

        if (!_isExpanded)
        {
            Width = size + BubbleWindowPadding;
            Height = size + BubbleWindowPadding;
            if (reposition && IsLoaded)
                PlaceBubbleAtEdge(Top);
        }
    }

    private void ApplyBubbleAppearance()
    {
        _bubbleGifTicker.Stop();
        _bubbleGifFrames = Array.Empty<BitmapSource>();
        _bubbleGifFrameDelays = Array.Empty<int>();
        _bubbleGifFrameIndex = 0;

        string? path = _dataService.ResolveStoredAssetPath(_appData.Settings.CustomBubbleImagePath);
        if (path is null || !TryLoadImageFrames(path, out BitmapSource[] frames, out int[] delays, out _))
        {
            BubbleImage.Source = null;
            BubbleImage.Visibility = Visibility.Collapsed;
            BubbleGlyph.Visibility = Visibility.Visible;
            return;
        }

        _bubbleGifFrames = frames;
        _bubbleGifFrameDelays = delays;
        double size = GetBubbleSize();
        _bubbleImageScale.ScaleX = _appData.Settings.CustomBubbleZoom;
        _bubbleImageScale.ScaleY = _appData.Settings.CustomBubbleZoom;
        double offsetScale = size * 0.0045;
        _bubbleImageOffset.X = _appData.Settings.CustomBubbleOffsetX * offsetScale;
        _bubbleImageOffset.Y = _appData.Settings.CustomBubbleOffsetY * offsetScale;
        BubbleGlyph.Visibility = Visibility.Collapsed;
        BubbleImage.Visibility = Visibility.Visible;
        BubbleImage.Source = _bubbleGifFrames[0];
        if (_bubbleGifFrames.Length > 1)
        {
            _bubbleGifTicker.Interval = TimeSpan.FromMilliseconds(_bubbleGifFrameDelays[0]);
            _bubbleGifTicker.Start();
        }
    }

    private bool PreflightImageFile(string path, string extension, out int width, out int height, out string error)
    {
        width = 0;
        height = 0;
        error = string.Empty;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream);
            switch (extension)
            {
                case ".png":
                {
                    byte[] header = reader.ReadBytes(24);
                    byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
                    if (header.Length < 24 || !header.Take(8).SequenceEqual(signature) ||
                        Encoding.ASCII.GetString(header, 12, 4) != "IHDR")
                    {
                        error = "Файл не имеет корректной структуры PNG.";
                        return false;
                    }
                    width = ReadBigEndianInt32(header, 16);
                    height = ReadBigEndianInt32(header, 20);
                    break;
                }
                case ".bmp":
                {
                    byte[] header = reader.ReadBytes(26);
                    if (header.Length < 26 || header[0] != (byte)'B' || header[1] != (byte)'M')
                    {
                        error = "Файл не имеет корректной структуры BMP.";
                        return false;
                    }
                    width = BitConverter.ToInt32(header, 18);
                    height = Math.Abs(BitConverter.ToInt32(header, 22));
                    break;
                }
                case ".jpg":
                case ".jpeg":
                    if (!TryReadJpegDimensions(reader, out width, out height))
                    {
                        error = "Не удалось найти корректный заголовок изображения JPEG.";
                        return false;
                    }
                    break;
                case ".gif":
                    return PreflightGif(reader, out error);
                default:
                    error = "Этот формат изображения не поддерживается.";
                    return false;
            }

            // Проверяем только корректность заголовка. Разрешение исходного файла
            // не ограничиваем: большие фото будут уменьшены самим WIC-декодером.
            if (width < 1 || height < 1)
            {
                error = "В изображении указаны некорректные размеры.";
                return false;
            }
            return true;
        }
        catch
        {
            error = "Не удалось прочитать заголовок изображения.";
            return false;
        }
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static bool TryReadJpegDimensions(BinaryReader reader, out int width, out int height)
    {
        width = 0;
        height = 0;
        Stream stream = reader.BaseStream;
        stream.Position = 0;
        if (reader.ReadByte() != 0xFF || reader.ReadByte() != 0xD8) return false;

        while (stream.Position + 4 < stream.Length)
        {
            byte markerPrefix;
            do { markerPrefix = reader.ReadByte(); } while (markerPrefix != 0xFF && stream.Position < stream.Length);
            if (stream.Position >= stream.Length) return false;
            byte marker;
            do { marker = reader.ReadByte(); } while (marker == 0xFF && stream.Position < stream.Length);
            if (marker is 0xD9 or 0xDA) return false;
            if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7) continue;

            ushort segmentLength = reader.ReadUInt16();
            segmentLength = (ushort)((segmentLength >> 8) | (segmentLength << 8));
            if (segmentLength < 2 || stream.Position + segmentLength - 2 > stream.Length) return false;
            bool isStartOfFrame = marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or
                0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
            if (isStartOfFrame)
            {
                _ = reader.ReadByte(); // precision
                height = reader.ReadUInt16();
                width = reader.ReadUInt16();
                return true;
            }
            stream.Position += segmentLength - 2;
        }
        return false;
    }

    private bool PreflightGif(BinaryReader reader, out string error)
    {
        error = string.Empty;
        Stream stream = reader.BaseStream;
        stream.Position = 0;
        byte[] header = reader.ReadBytes(13);
        if (header.Length != 13 || Encoding.ASCII.GetString(header, 0, 3) != "GIF" ||
            (Encoding.ASCII.GetString(header, 0, 6) is not "GIF87a" and not "GIF89a"))
        {
            error = "Файл не имеет корректной структуры GIF.";
            return false;
        }

        int canvasWidth = header[6] | (header[7] << 8);
        int canvasHeight = header[8] | (header[9] << 8);
        int packed = header[10];
        if (canvasWidth < 1 || canvasHeight < 1)
        {
            error = "В GIF указаны некорректные размеры холста.";
            return false;
        }
        if ((packed & 0x80) != 0)
            stream.Position += 3L * (1 << ((packed & 0x07) + 1));

        int frameCount = 0;
        while (stream.Position < stream.Length)
        {
            int introducer = reader.ReadByte();
            if (introducer == 0x3B) break; // trailer
            if (introducer == 0x21)
            {
                if (stream.Position >= stream.Length) return false;
                _ = reader.ReadByte(); // extension label
                if (!SkipGifSubBlocks(reader))
                {
                    error = "Структура GIF повреждена.";
                    return false;
                }
                continue;
            }
            if (introducer != 0x2C || stream.Position + 9 > stream.Length)
            {
                error = "Структура GIF повреждена.";
                return false;
            }

            _ = reader.ReadUInt16(); // left
            _ = reader.ReadUInt16(); // top
            int frameWidth = reader.ReadUInt16();
            int frameHeight = reader.ReadUInt16();
            int imagePacked = reader.ReadByte();
            if (frameWidth < 1 || frameHeight < 1)
            {
                error = "В GIF найден кадр с некорректным размером.";
                return false;
            }
            frameCount++;
            if (frameCount > MaxGifFrames)
            {
                error = $"В GIF слишком много кадров. Максимум — {MaxGifFrames}.";
                return false;
            }
            if ((imagePacked & 0x80) != 0)
                stream.Position += 3L * (1 << ((imagePacked & 0x07) + 1));
            if (stream.Position >= stream.Length)
            {
                error = "Структура GIF повреждена.";
                return false;
            }
            _ = reader.ReadByte(); // LZW minimum code size
            if (!SkipGifSubBlocks(reader))
            {
                error = "Структура GIF повреждена.";
                return false;
            }
        }

        if (frameCount == 0)
        {
            error = "В GIF не найдено ни одного кадра.";
            return false;
        }
        return true;
    }

    private static bool SkipGifSubBlocks(BinaryReader reader)
    {
        Stream stream = reader.BaseStream;
        while (stream.Position < stream.Length)
        {
            int length = reader.ReadByte();
            if (length == 0) return true;
            if (stream.Position + length > stream.Length) return false;
            stream.Position += length;
        }
        return false;
    }

    private bool TryLoadImageFrames(string path, out BitmapSource[] frames, out int[] delays, out string error)
    {
        frames = Array.Empty<BitmapSource>();
        delays = Array.Empty<int>();
        error = string.Empty;
        try
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".gif")
            {
                error = "Поддерживаются PNG, JPG, BMP и GIF.";
                return false;
            }

            var info = new FileInfo(path);
            long maximumFileSize = GetMaximumImageFileSize();
            if (info.Length <= 0 || info.Length > maximumFileSize)
            {
                error = $"Файл изображения должен быть не больше {maximumFileSize / (1024.0 * 1024.0):0} МБ.";
                return false;
            }
            if (!PreflightImageFile(path, extension, out int sourceWidth, out int sourceHeight, out error)) return false;

            // Для обычных фотографий используем DecodePixelWidth/Height: WIC
            // уменьшает изображение во время декодирования, а не после выделения
            // памяти под полноразмерный bitmap.
            if (!string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase))
            {
                using var stillStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                if (Math.Max(sourceWidth, sourceHeight) > MaxStillImageDecodeDimension)
                {
                    if (sourceWidth >= sourceHeight)
                        bitmap.DecodePixelWidth = MaxStillImageDecodeDimension;
                    else
                        bitmap.DecodePixelHeight = MaxStillImageDecodeDimension;
                }
                bitmap.StreamSource = stillStream;
                bitmap.EndInit();
                if (bitmap.CanFreeze) bitmap.Freeze();
                frames = new BitmapSource[] { bitmap };
                delays = new[] { 100 };
                return true;
            }

            // Для GIF декодер работает в режиме OnDemand: кадры обрабатываются
            // последовательно и уменьшаются до сборки общего холста.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            if (decoder.Frames.Count == 0 || decoder.Frames.Count > MaxGifFrames)
            {
                error = $"В GIF должно быть от 1 до {MaxGifFrames} кадров.";
                return false;
            }

            BitmapMetadata? logicalMetadata = decoder.Metadata as BitmapMetadata;
            int logicalWidth = ReadGifMetadataInt(logicalMetadata, "/logscrdesc/Width", 0);
            int logicalHeight = ReadGifMetadataInt(logicalMetadata, "/logscrdesc/Height", 0);
            int maxRight = 1;
            int maxBottom = 1;
            var frameInfo = new List<(BitmapFrame Frame, int Left, int Top, int Delay, int Disposal)>(decoder.Frames.Count);

            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (frame.PixelWidth < 1 || frame.PixelHeight < 1)
                {
                    error = "В GIF найден кадр с некорректным размером.";
                    return false;
                }

                BitmapMetadata? metadata = frame.Metadata as BitmapMetadata;
                int left = Math.Max(0, ReadGifMetadataInt(metadata, "/imgdesc/Left", 0));
                int top = Math.Max(0, ReadGifMetadataInt(metadata, "/imgdesc/Top", 0));
                int delayHundredths = ReadGifMetadataInt(metadata, "/grctlext/Delay", 10);
                int delay = Math.Clamp(delayHundredths * 10, 40, 2000);
                int disposal = Math.Clamp(ReadGifMetadataInt(metadata, "/grctlext/Disposal", 0), 0, 3);
                frameInfo.Add((frame, left, top, delay, disposal));
                maxRight = Math.Max(maxRight, left + frame.PixelWidth);
                maxBottom = Math.Max(maxBottom, top + frame.PixelHeight);
            }

            int sourceCanvasWidth = logicalWidth > 0 ? logicalWidth : maxRight;
            int sourceCanvasHeight = logicalHeight > 0 ? logicalHeight : maxBottom;
            if (sourceCanvasWidth < maxRight || sourceCanvasHeight < maxBottom || sourceCanvasWidth < 1 || sourceCanvasHeight < 1)
            {
                error = "Размер логического холста GIF повреждён.";
                return false;
            }

            // Ограничение относится только к объёму кадров, которые хранятся
            // в памяти приложения, а не к разрешению исходного GIF. При большом
            // исходнике уменьшаем масштаб кадров пропорционально их количеству.
            long sourceFramePixels = (long)sourceCanvasWidth * sourceCanvasHeight * frameInfo.Count;
            double scale = sourceFramePixels > MaxGifDecodedPixelsTotal
                ? Math.Sqrt(MaxGifDecodedPixelsTotal / (double)sourceFramePixels)
                : 1.0;
            scale = Math.Min(scale, 1.0);

            int canvasWidth = Math.Max(1, (int)Math.Round(sourceCanvasWidth * scale));
            int canvasHeight = Math.Max(1, (int)Math.Round(sourceCanvasHeight * scale));
            // Небольшая страховка от погрешности округления итогового бюджета.
            while ((long)canvasWidth * canvasHeight * frameInfo.Count > MaxGifDecodedPixelsTotal)
            {
                scale *= 0.99;
                canvasWidth = Math.Max(1, (int)Math.Round(sourceCanvasWidth * scale));
                canvasHeight = Math.Max(1, (int)Math.Round(sourceCanvasHeight * scale));
            }

            bool framesAreFullCanvas = frameInfo.All(item => item.Left == 0 && item.Top == 0 &&
                item.Frame.PixelWidth == sourceCanvasWidth && item.Frame.PixelHeight == sourceCanvasHeight);
            int canvasStride = checked(canvasWidth * 4);
            byte[] canvasPixels = new byte[checked(canvasStride * canvasHeight)];
            byte[]? previousCanvas = null;
            (int Left, int Top, int Width, int Height) previousRect = default;
            int previousDisposal = 0;
            byte[] backgroundPixel = new byte[4];
            var frameList = new List<BitmapSource>(frameInfo.Count);
            var delayList = new List<int>(frameInfo.Count);

            for (int index = 0; index < frameInfo.Count; index++)
            {
                var current = frameInfo[index];

                if (index > 0)
                {
                    if (previousDisposal == 2)
                    {
                        ClearGifRectangle(canvasPixels, canvasStride, canvasWidth, canvasHeight, previousRect, backgroundPixel);
                    }
                    else if (previousDisposal == 3 && previousCanvas is not null)
                    {
                        canvasPixels = (byte[])previousCanvas.Clone();
                    }
                }

                byte[]? canvasBeforeCurrent = current.Disposal == 3 ? (byte[])canvasPixels.Clone() : null;
                int targetFrameWidth = Math.Max(1, (int)Math.Round(current.Frame.PixelWidth * scale));
                int targetFrameHeight = Math.Max(1, (int)Math.Round(current.Frame.PixelHeight * scale));
                BitmapSource scaledFrame = current.Frame;
                if (scaledFrame.PixelWidth != targetFrameWidth || scaledFrame.PixelHeight != targetFrameHeight)
                {
                    var transformed = new TransformedBitmap(current.Frame,
                        new ScaleTransform(targetFrameWidth / (double)current.Frame.PixelWidth,
                                           targetFrameHeight / (double)current.Frame.PixelHeight));
                    if (transformed.CanFreeze) transformed.Freeze();
                    scaledFrame = transformed;
                }

                BitmapSource converted = new FormatConvertedBitmap(scaledFrame, PixelFormats.Pbgra32, null, 0);
                int sourceStride = checked(converted.PixelWidth * 4);
                byte[] sourcePixels = new byte[checked(sourceStride * converted.PixelHeight)];
                converted.CopyPixels(sourcePixels, sourceStride, 0);
                int scaledLeft = (int)Math.Round(current.Left * scale);
                int scaledTop = (int)Math.Round(current.Top * scale);
                if (framesAreFullCanvas)
                {
                    Buffer.BlockCopy(sourcePixels, 0, canvasPixels, 0, Math.Min(sourcePixels.Length, canvasPixels.Length));
                }
                else
                {
                    CompositeGifFrame(canvasPixels, canvasStride, canvasWidth, canvasHeight,
                        sourcePixels, sourceStride, converted.PixelWidth, converted.PixelHeight, scaledLeft, scaledTop);
                }

                if (index == 0 && canvasPixels.Length >= 4 && canvasPixels[3] > 0)
                    Buffer.BlockCopy(canvasPixels, 0, backgroundPixel, 0, 4);

                BitmapSource composed = BitmapSource.Create(canvasWidth, canvasHeight, 96, 96,
                    PixelFormats.Pbgra32, null, (byte[])canvasPixels.Clone(), canvasStride);
                if (composed.CanFreeze) composed.Freeze();
                frameList.Add(composed);
                delayList.Add(current.Delay);

                previousCanvas = canvasBeforeCurrent;
                previousRect = (scaledLeft, scaledTop, converted.PixelWidth, converted.PixelHeight);
                previousDisposal = current.Disposal;
            }

            frames = frameList.ToArray();
            delays = delayList.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Не удалось прочитать изображение: {ex.Message}";
            frames = Array.Empty<BitmapSource>();
            delays = Array.Empty<int>();
            return false;
        }
    }

    private static int ReadGifMetadataInt(BitmapMetadata? metadata, string query, int fallback)
    {
        try
        {
            if (metadata is null || !metadata.ContainsQuery(query)) return fallback;
            object? value = metadata.GetQuery(query);
            return value is null ? fallback : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return fallback;
        }
    }

    private static void ClearGifRectangle(byte[] canvas, int canvasStride, int canvasWidth, int canvasHeight,
        (int Left, int Top, int Width, int Height) rect, byte[] background)
    {
        int left = Math.Clamp(rect.Left, 0, canvasWidth);
        int top = Math.Clamp(rect.Top, 0, canvasHeight);
        int right = Math.Clamp(rect.Left + rect.Width, 0, canvasWidth);
        int bottom = Math.Clamp(rect.Top + rect.Height, 0, canvasHeight);
        for (int y = top; y < bottom; y++)
        {
            for (int x = left; x < right; x++)
            {
                int offset = y * canvasStride + x * 4;
                canvas[offset] = background[0];
                canvas[offset + 1] = background[1];
                canvas[offset + 2] = background[2];
                canvas[offset + 3] = background[3];
            }
        }
    }

    private static void CompositeGifFrame(byte[] destination, int destinationStride, int canvasWidth, int canvasHeight,
        byte[] source, int sourceStride, int sourceWidth, int sourceHeight, int left, int top)
    {
        for (int y = 0; y < sourceHeight; y++)
        {
            int targetY = top + y;
            if (targetY < 0 || targetY >= canvasHeight) continue;
            for (int x = 0; x < sourceWidth; x++)
            {
                int targetX = left + x;
                if (targetX < 0 || targetX >= canvasWidth) continue;

                int src = y * sourceStride + x * 4;
                int dst = targetY * destinationStride + targetX * 4;
                int alpha = source[src + 3];
                if (alpha == 0) continue;
                if (alpha == 255)
                {
                    destination[dst] = source[src];
                    destination[dst + 1] = source[src + 1];
                    destination[dst + 2] = source[src + 2];
                    destination[dst + 3] = 255;
                    continue;
                }

                int inverseAlpha = 255 - alpha;
                destination[dst] = (byte)Math.Min(255, source[src] + (destination[dst] * inverseAlpha + 127) / 255);
                destination[dst + 1] = (byte)Math.Min(255, source[src + 1] + (destination[dst + 1] * inverseAlpha + 127) / 255);
                destination[dst + 2] = (byte)Math.Min(255, source[src + 2] + (destination[dst + 2] * inverseAlpha + 127) / 255);
                destination[dst + 3] = (byte)Math.Min(255, alpha + (destination[dst + 3] * inverseAlpha + 127) / 255);
            }
        }
    }

    private void BubbleGifTicker_Tick(object? sender, EventArgs e)
    {
        if (_bubbleGifFrames.Length < 2) { _bubbleGifTicker.Stop(); return; }
        _bubbleGifFrameIndex = (_bubbleGifFrameIndex + 1) % _bubbleGifFrames.Length;
        BubbleImage.Source = _bubbleGifFrames[_bubbleGifFrameIndex];
        _bubbleGifTicker.Interval = TimeSpan.FromMilliseconds(_bubbleGifFrameDelays[_bubbleGifFrameIndex]);
    }

    private void SynchronizeStartupSetting()
    {
        bool enabled = StartupManager.IsEnabled();
        if (_appData.Settings.StartWithWindows != enabled)
        {
            _appData.Settings.StartWithWindows = enabled;
            SaveData();
        }
    }

    private void ShowFirstRunStoragePrompt()
    {
        ShowTwoChoice(
            "Где хранить сохранения?",
            "Выбери отдельную папку для данных Orbiq или укажи папку, в которой уже лежат старые сохранения. Можно создать новую папку прямо в окне выбора.",
            "Выбрать папку",
            ChooseInitialStorageFolder,
            "По умолчанию",
            () => CompleteStorageSetup(_dataService.DefaultDataDirectory));
    }

    private void ChooseInitialStorageFolder()
    {
        string? selected = PickStorageFolder("Выбери или создай отдельную папку для сохранений Orbiq.");
        if (selected is null)
        {
            ShowFirstRunStoragePrompt();
            return;
        }
        CompleteStorageSetup(selected);
    }

    private void CompleteStorageSetup(string directory)
    {
        try
        {
            _dataService.SetDataDirectory(directory);
            _appData = _dataService.Load();
            NormalizeAppData();
            LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
            ApplyTheme();
            ApplyShellLocalization();
            ApplyWelcomeText();
            SynchronizeStartupSetting();
            _reminderTicker.Start();
            RefreshGlobalHotkeys(showErrors: false);
            NavigateTo("Clock");
            SaveData();
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось выбрать папку", ex.Message);
            ShowFirstRunStoragePrompt();
        }
    }

    private string? PickStorageFolder(string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_dataService.DataDirectory)
                ? _dataService.DataDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    // ----------------------------- ОКНО И ТРЕЙ -----------------------------

    private void PlaceBubbleAtEdge(double preferredTop)
    {
        Rect area = SystemParameters.WorkArea;
        double size = GetBubbleSize();
        double windowSize = size + BubbleWindowPadding;
        Bubble.Width = size;
        Bubble.Height = size;
        Bubble.CornerRadius = new CornerRadius(size / 2);
        BubbleImage.Width = size;
        BubbleImage.Height = size;
        BubbleGlyph.Width = size * 0.86;
        BubbleGlyph.Height = size * 0.86;
        Width = windowSize;
        Height = windowSize;
        UpdateBubbleClip();

        Left = _bubbleOnRight
            ? Math.Max(area.Left + BubbleEdgePadding, area.Right - Width - BubbleEdgePadding)
            : Math.Min(area.Right - Width - BubbleEdgePadding, area.Left + BubbleEdgePadding);
        double minimumTop = area.Top + BubbleEdgePadding;
        double maximumTop = Math.Max(minimumTop, area.Bottom - Height - BubbleEdgePadding);
        Top = Math.Clamp(preferredTop, minimumTop, maximumTop);
    }

    private void ShowBubble()
    {
        double targetSize = GetBubbleWindowSize();
        double preferredTop = Height > targetSize + 1 ? Top + (Height - targetSize) / 2 : Top;
        _isExpanded = false;
        Panel.Visibility = Visibility.Collapsed;
        DialogOverlay.Visibility = Visibility.Collapsed;
        Bubble.Visibility = Visibility.Visible;
        PlaceBubbleAtEdge(preferredTop);
        if (_trayIcon is not null) _trayIcon.Visible = false;
        Show();
    }

    private void Bubble_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => ExpandPanel();

    private void ExpandPanel()
    {
        if (_isExpanded) return;
        _isExpanded = true;
        Rect area = SystemParameters.WorkArea;
        double oldCenter = Top + Height / 2;
        Width = 850;
        Height = 670;
        Left = _bubbleOnRight ? area.Right - Width - 12 : area.Left + 12;
        Top = Math.Clamp(oldCenter - Height / 2, area.Top + 8, area.Bottom - Height - 8);
        Panel.RenderTransformOrigin = _bubbleOnRight ? new Point(1, 0.5) : new Point(0, 0.5);
        Bubble.Visibility = Visibility.Collapsed;
        Panel.Visibility = Visibility.Visible;
        PanelScale.ScaleX = 0.05;
        PanelScale.ScaleY = 0.05;
        Panel.Opacity = 0;
        if (_trayIcon is not null) _trayIcon.Visible = false;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.05, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210)));
        Show();
        Activate();
    }

    private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseToBubble();

    private void CollapseToBubble()
    {
        if (!_isExpanded) return;
        DialogOverlay.Visibility = Visibility.Collapsed;
        Rect workArea = SystemParameters.WorkArea;
        double rightDistance = Math.Abs(workArea.Right - (Left + Width));
        double leftDistance = Math.Abs(Left - workArea.Left);
        _bubbleOnRight = rightDistance <= leftDistance;
        Panel.RenderTransformOrigin = _bubbleOnRight ? new Point(1, 0.5) : new Point(0, 0.5);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var scaleX = new DoubleAnimation(PanelScale.ScaleX, 0.05, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing };
        var scaleY = new DoubleAnimation(PanelScale.ScaleY, 0.05, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing };
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(Panel.Opacity, 0, TimeSpan.FromMilliseconds(160)));
        scaleX.Completed += (_, _) =>
        {
            double preferredTop = Top + (Height - GetBubbleWindowSize()) / 2;
            Panel.Visibility = Visibility.Collapsed;
            PanelScale.ScaleX = 0.05;
            PanelScale.ScaleY = 0.05;
            Panel.Opacity = 0;
            _isExpanded = false;
            Bubble.Visibility = Visibility.Visible;
            PlaceBubbleAtEdge(preferredTop);
            if (_trayIcon is not null) _trayIcon.Visible = false;
        };
        PanelScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        PanelScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isExpanded || DialogOverlay.Visibility == Visibility.Visible) return;
        if (IsInteractiveElement(e.OriginalSource as DependencyObject)) return;
        Point point = e.GetPosition(this);
        bool onEdge = point.X <= 12 || point.Y <= 12 || point.X >= ActualWidth - 12 || point.Y >= ActualHeight - 12;
        bool onHeader = point.Y <= 78;
        if (!onEdge && !onHeader) return;

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
            // Windows may end a drag before the mouse is released; no action is needed.
        }
    }

    private static bool IsInteractiveElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ButtonBase or TextBoxBase or ComboBox or Selector or ScrollBar) return true;
            if (current is Window) break;
            try { current = VisualTreeHelper.GetParent(current); }
            catch { current = LogicalTreeHelper.GetParent(current); }
        }
        return false;
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e) => HideToTray();

    private void HideToTray()
    {
        SaveData();
        DialogOverlay.Visibility = Visibility.Collapsed;
        if (_trayIcon is not null) _trayIcon.Visible = true;
        Hide();
    }

    private void InitializeTrayIcon()
    {
        Drawing.Icon iconToUse = Drawing.SystemIcons.Application;
        try
        {
            // Иконка встроена в сборку: после публикации Orbiq не нужна внешняя папка Assets.
            using Stream? iconStream = typeof(MainWindow).Assembly
                .GetManifestResourceStream("Orbiq.Assets.orbiq.ico");
            if (iconStream is not null)
            {
                _trayImageIcon = new Drawing.Icon(iconStream);
                iconToUse = _trayImageIcon;
            }
        }
        catch
        {
            // Используем стандартную иконку Windows, если встроенный ресурс недоступен.
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = iconToUse,
            Text = LocalizationService.Translate("Orbiq — открыть панель"),
            Visible = false
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(LocalizationService.Translate("Открыть Orbiq"), null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Translate("Выход"), null, (_, _) => Dispatcher.Invoke(RequestClose));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowBubble();
        Activate();
    }

    private void ClosePrompt_Click(object sender, RoutedEventArgs e) => RequestClose();

    private void RequestClose()
    {
        Show();
        if (!_isExpanded) ExpandPanel();
        ShowConfirm("Закрыть приложение?", "Все заметки и настройки сохранены. Напоминания не будут срабатывать, пока Orbiq закрыт.", ExitApplication);
    }

    private void ConfirmClose_Click(object sender, RoutedEventArgs e) => ExitApplication();

    private void CancelClose_Click(object sender, RoutedEventArgs e) => DialogOverlay.Visibility = Visibility.Collapsed;

    private void ExitApplication() => ShutdownApplication(saveData: true);

    private void ShutdownApplication(bool saveData)
    {
        if (saveData) SaveData();
        _allowClose = true;
        _skipSaveOnClose = true;
        _clockTicker.Stop();
        _reminderTicker.Stop();
        _pomodoroTicker.Stop();
        _monitorTicker.Stop();
        UnregisterGlobalHotkeys();
        _bubbleGifTicker.Stop();
        try { _customReminderPlayer?.Stop(); _customReminderPlayer?.Close(); } catch { }
        _customReminderPlayer = null;
        _toastWindow?.Close();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayImageIcon?.Dispose();
        _trayImageIcon = null;
        Close();
        Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            RequestClose();
        }
        else if (!_skipSaveOnClose) SaveData();
    }

    // ----------------------------- ДИАЛОГИ ВНУТРИ ПРИЛОЖЕНИЯ -----------------------------

    private void ShowNotice(string title, string message)
    {
        DialogCard.Width = 490;
        DialogTitle.Text = LocalizationService.Translate(title);
        DialogMessage.Text = LocalizationService.Translate(message);
        DialogAcceptButton.Content = LocalizationService.Translate("Понятно");
        DialogCancelButton.Visibility = Visibility.Collapsed;
        DialogThirdButton.Visibility = Visibility.Collapsed;
        _dialogConfirmAction = null;
        _dialogCancelAction = null;
        _dialogThirdAction = null;
        DialogOverlay.Visibility = Visibility.Visible;
    }

    private void ShowConfirm(string title, string message, Action confirmAction)
    {
        ShowTwoChoice(title, message, "Да", confirmAction, "Нет", null);
    }

    private void ShowTwoChoice(string title, string message,
        string primaryText, Action? primaryAction,
        string secondaryText, Action? secondaryAction)
    {
        DialogCard.Width = 490;
        DialogTitle.Text = LocalizationService.Translate(title);
        DialogMessage.Text = LocalizationService.Translate(message);
        DialogAcceptButton.Content = LocalizationService.Translate(primaryText);
        DialogCancelButton.Content = LocalizationService.Translate(secondaryText);
        DialogCancelButton.Visibility = Visibility.Visible;
        DialogThirdButton.Visibility = Visibility.Collapsed;
        _dialogConfirmAction = primaryAction;
        _dialogCancelAction = secondaryAction;
        _dialogThirdAction = null;
        DialogOverlay.Visibility = Visibility.Visible;
    }

    private void ShowThreeChoice(string title, string message,
        string primaryText, Action primaryAction,
        string secondaryText, Action? secondaryAction,
        string thirdText, Action thirdAction)
    {
        // У окна выбора способа удаления три длинные кнопки — даём им больше места.
        DialogCard.Width = 610;
        DialogTitle.Text = LocalizationService.Translate(title);
        DialogMessage.Text = LocalizationService.Translate(message);
        DialogAcceptButton.Content = LocalizationService.Translate(primaryText);
        DialogCancelButton.Content = LocalizationService.Translate(secondaryText);
        DialogCancelButton.Visibility = Visibility.Visible;
        DialogThirdButton.Content = LocalizationService.Translate(thirdText);
        DialogThirdButton.Visibility = Visibility.Visible;
        _dialogConfirmAction = primaryAction;
        _dialogCancelAction = secondaryAction;
        _dialogThirdAction = thirdAction;
        DialogOverlay.Visibility = Visibility.Visible;
    }

    private void DialogAccept_Click(object sender, RoutedEventArgs e)
    {
        Action? action = _dialogConfirmAction;
        ClearDialog();
        action?.Invoke();
    }

    private void DialogCancel_Click(object sender, RoutedEventArgs e)
    {
        Action? action = _dialogCancelAction;
        ClearDialog();
        action?.Invoke();
    }

    private void DialogThird_Click(object sender, RoutedEventArgs e)
    {
        Action? action = _dialogThirdAction;
        ClearDialog();
        action?.Invoke();
    }

    private void ClearDialog()
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        DialogCard.Width = 490;
        _dialogConfirmAction = null;
        _dialogCancelAction = null;
        _dialogThirdAction = null;
        DialogThirdButton.Visibility = Visibility.Collapsed;
    }

    private bool SaveData()
    {
        try
        {
            _dataService.Save(_appData);
            return true;
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось сохранить данные", ex.Message);
            return false;
        }
    }

    // ----------------------------- ПАПКА СОХРАНЕНИЙ -----------------------------

    private void ChangeStorageFolder()
    {
        string? selected = PickStorageFolder("Выбери новую отдельную папку для сохранений Orbiq.");
        if (selected is null) return;

        string oldDirectory = Path.GetFullPath(_dataService.DataDirectory);
        string newDirectory = Path.GetFullPath(selected);
        if (string.Equals(oldDirectory.TrimEnd(Path.DirectorySeparatorChar),
            newDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            ShowNotice("Папка не изменена", "Эта папка уже используется для сохранений Orbiq.");
            return;
        }

        if (IsSameOrChildDirectory(oldDirectory, newDirectory) || IsSameOrChildDirectory(newDirectory, oldDirectory))
        {
            ShowNotice("Нельзя перенести сохранения", "Новая папка не должна находиться внутри старой папки или наоборот. Выбери отдельную папку.");
            return;
        }
        try { _dataService.ValidateDataDirectory(newDirectory); }
        catch (Exception ex)
        {
            ShowNotice("Нельзя использовать эту папку", ex.Message);
            return;
        }

        bool destinationHasFiles;
        try
        {
            destinationHasFiles = Directory.Exists(newDirectory) && Directory.EnumerateFileSystemEntries(newDirectory).Any();
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось прочитать новую папку", ex.Message);
            return;
        }

        if (destinationHasFiles)
        {
            ShowConfirm("В новой папке уже есть файлы",
                "Orbiq перенесёт туда все файлы из текущей папки. Файлы с одинаковыми именами будут заменены версиями из текущей папки Orbiq. Продолжить?",
                () => MigrateStorageFolder(oldDirectory, newDirectory, overwrite: true));
        }
        else
        {
            MigrateStorageFolder(oldDirectory, newDirectory, overwrite: false);
        }
    }

    private void MigrateStorageFolder(string oldDirectory, string newDirectory, bool overwrite)
    {
        try
        {
            CopyDirectoryContents(oldDirectory, newDirectory, overwrite);
            _dataService.SetDataDirectory(newDirectory);
            _dataService.Save(_appData);
            NavigateTo("Settings");

            ShowTwoChoice(
                "Сохранения перенесены",
                "Все файлы Orbiq скопированы в новую папку. Что сделать со старой папкой? При удалении будет удалено всё её содержимое, включая файлы, не относящиеся к Orbiq.",
                "Удалить старую папку",
                () => DeleteOldStorageFolder(oldDirectory),
                "Оставить старую папку",
                () => NavigateTo("Settings"));
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось перенести сохранения", ex.Message);
        }
    }

    private static void CopyDirectoryContents(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
        {
            if (string.Equals(Path.GetFileName(sourceFile), ".minihub-folder", StringComparison.OrdinalIgnoreCase))
                continue;

            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            if (File.Exists(destinationFile) && !overwrite)
                throw new IOException($"Файл уже существует: {destinationFile}");
            File.Copy(sourceFile, destinationFile, overwrite);
        }

        foreach (string sourceSubdirectory in Directory.GetDirectories(sourceDirectory))
        {
            if ((File.GetAttributes(sourceSubdirectory) & FileAttributes.ReparsePoint) != 0)
                continue;
            string destinationSubdirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory));
            CopyDirectoryContents(sourceSubdirectory, destinationSubdirectory, overwrite);
        }
    }

    private static bool IsSameOrChildDirectory(string parentDirectory, string candidateDirectory)
    {
        string parent = Path.GetFullPath(parentDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(candidateDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private void DeleteOldStorageFolder(string oldDirectory)
    {
        try
        {
            if (DataService.IsSafeDirectoryForRecursiveDeletion(oldDirectory))
            {
                DataService.DeleteDirectoryContents(oldDirectory);
                ShowNotice("Старая папка удалена", "Сохранения Orbiq находятся в новой папке. Старая папка и её содержимое удалены.");
            }
            else
            {
                _dataService.DeleteMiniHubFilesFromDirectory(oldDirectory);
                ShowNotice("Старая папка очищена частично", "Для безопасности Orbiq удалил только собственные файлы сохранений. Остальные файлы и папка оставлены.");
            }
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось удалить старую папку", ex.Message);
        }
        NavigateTo("Settings");
    }

    // ----------------------------- УДАЛЕНИЕ ПРОГРАММЫ -----------------------------

    private void PromptResetSettings()
    {
        ShowConfirm(
            "Сбросить настройки Orbiq?",
            "Будут восстановлены стандартная тёмная тема, приветствие «Приятного использования», мягкий сигнал с громкостью 50%, размер кружка 50%, масштаб изображения 1×, центральное кадрирование, пустые горячие клавиши и выключенная автозагрузка. Папка сохранений, папка резервных копий, резервные копии и личные данные останутся нетронутыми.",
            ResetSettingsToDefault);
    }

    private void ResetSettingsToDefault()
    {
        AppSettings previous = _appData.Settings;
        string backupDirectory = previous.BackupDirectory ?? string.Empty;
        bool automaticBackupsEnabled = previous.AutomaticBackupsEnabled;
        string lastBackupDate = previous.LastAutomaticBackupDate ?? string.Empty;
        try
        {
            StartupManager.SetEnabled(false);
        }
        catch (Exception ex)
        {
            string resetError = LocalizationService.Translate("Настройки не были сброшены, потому что Windows не подтвердила отключение автозагрузки. {details}")
                .Replace("{details}", ex.Message, StringComparison.Ordinal);
            ShowNotice("Не удалось отключить автозагрузку", resetError);
            return;
        }

        var defaults = new AppSettings
        {
            Theme = "Тёмная",
            WelcomeText = "Приятного использования",
            NotificationSound = "Мягкий сигнал",
            NotificationVolume = 50,
            BubbleSize = GetDefaultBubbleSize(),
            CustomBubbleZoom = 1.0,
            CustomBubbleOffsetX = 0,
            CustomBubbleOffsetY = 0,
            CustomReminderSoundPath = string.Empty,
            CustomBubbleImagePath = string.Empty,
            StartWithWindows = false,
            Hotkeys = new Dictionary<string, string>(),
            PomodoroWorkMinutes = 25,
            PomodoroShortBreakMinutes = 5,
            PomodoroLongBreakMinutes = 15,
            PomodoroCyclesBeforeLongBreak = 4,
            PomodoroCompletedSessions = 0,
            CalculatorHistory = new List<string>(),
            CalculatorMemory = 0,
            BackupDirectory = backupDirectory,
            AutomaticBackupsEnabled = automaticBackupsEnabled,
            LastAutomaticBackupDate = lastBackupDate
        };

        _appData.Settings = defaults;
        LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
        if (!SaveData())
        {
            _appData.Settings = previous;
            try { StartupManager.SetEnabled(previous.StartWithWindows); } catch { }
            return;
        }

        _pomodoroTicker.Stop();
        _pomodoroRunning = false;
        _pomodoroPhase = "Работа";
        _pomodoroPhaseDuration = TimeSpan.FromMinutes(defaults.PomodoroWorkMinutes);
        _pomodoroRemaining = _pomodoroPhaseDuration;
        _secondMode = false;
        _degreeMode = true;
        ApplyTheme();
        ApplyShellLocalization();
        InitializeQuickActionsMenu();
        ApplyWelcomeText();
        ApplyBubbleSize();
        ApplyBubbleAppearance();
        UnregisterGlobalHotkeys();
        RefreshGlobalHotkeys(showErrors: false);
        NavigateTo("Settings");
        ShowNotice("Настройки сброшены", "Orbiq вернулся к стандартным параметрам. Папки сохранений и резервных копий, заметки, напоминания и календарь сохранены.");
    }

    private void ShowUninstallOptions()
    {
        ShowThreeChoice(
            "Удаление Orbiq",
            "Можно удалить только файлы приложения, оставив заметки и настройки, либо удалить приложение вместе с сохранениями. Если запускаешь проект через VS Code, будут удалены только собранные файлы в bin/Debug или publish — исходный код проекта останется нетронутым.",
            "Только приложение",
            () => CompleteUninstall(deleteAllSaves: false),
            "Отмена",
            null,
            "Приложение и данные",
            ConfirmDeleteAllData);
    }

    private void ConfirmDeleteAllData()
    {
        ShowConfirm(
            "Удалить все сохранения?",
            "Заметки, напоминания и настройки Orbiq будут удалены без возможности восстановления. Посторонние файлы в выбранной папке сохранений останутся нетронутыми. Продолжить?",
            () => CompleteUninstall(deleteAllSaves: true));
    }

    private void CompleteUninstall(bool deleteAllSaves)
    {
        bool startupWasEnabled = StartupManager.IsEnabled();
        Process? cleanupProcess = null;
        try
        {
            _dataService.Save(_appData);
            StartupManager.RemoveEntry();
            _appData.Settings.StartWithWindows = false;
            if (!deleteAllSaves) _dataService.Save(_appData);

            cleanupProcess = ScheduleApplicationFolderRemoval();
            if (cleanupProcess is null)
            {
                RestoreStartupAfterFailedUninstall(startupWasEnabled);
                ShowNotice("Не удалось подготовить удаление", "Windows не смогла запустить фоновую очистку. Приложение и сохранения оставлены без изменений.");
                return;
            }

            if (deleteAllSaves)
                _dataService.DeleteAllSavedDataFiles(_appData.Settings.BackupDirectory);

            ShutdownApplication(saveData: false);
        }
        catch (Exception ex)
        {
            try
            {
                if (cleanupProcess is { HasExited: false }) cleanupProcess.Kill();
            }
            catch { /* Если фоновая очистка уже завершилась, продолжаем восстановление данных. */ }

            try
            {
                _dataService.SaveStorageConfiguration();
                _dataService.Save(_appData);
                RestoreStartupAfterFailedUninstall(startupWasEnabled);
            }
            catch { /* Показываем исходную ошибку; не скрываем её вторичной ошибкой восстановления. */ }
            ShowNotice("Не удалось удалить Orbiq", ex.Message);
        }
        finally
        {
            cleanupProcess?.Dispose();
        }
    }

    private void RestoreStartupAfterFailedUninstall(bool startupWasEnabled)
    {
        if (startupWasEnabled)
        {
            StartupManager.SetEnabled(true);
            _appData.Settings.StartWithWindows = true;
        }
        else
        {
            _appData.Settings.StartWithWindows = false;
        }
        SaveData();
    }

    private static Process? ScheduleApplicationFolderRemoval()
    {
        string applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetPathRoot(applicationDirectory) ?? string.Empty;
        if (string.Equals(applicationDirectory, root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return null;

        // Проверяем, что это именно папка собранного MiniHub, а не случайная рабочая папка.
        if (!File.Exists(Path.Combine(applicationDirectory, "Orbiq.dll")) &&
            !File.Exists(Path.Combine(applicationDirectory, "Orbiq.exe")) &&
            !File.Exists(Path.Combine(applicationDirectory, "MiniHub.dll")) &&
            !File.Exists(Path.Combine(applicationDirectory, "MiniHub.exe")))
            return null;

        string scriptPath = Path.Combine(Path.GetTempPath(), $"Orbiq-uninstall-{Environment.ProcessId}.ps1");
        string encodedDirectory = Convert.ToBase64String(Encoding.UTF8.GetBytes(applicationDirectory));
        string script = $"$target = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedDirectory}')){Environment.NewLine}" +
                        "$targetProcessId = " + Environment.ProcessId + Environment.NewLine +
                        "while (Get-Process -Id $targetProcessId -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 500 }" + Environment.NewLine +
                        "Start-Sleep -Milliseconds 300" + Environment.NewLine +
                        "if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue }" + Environment.NewLine;
        File.WriteAllText(scriptPath, script, Encoding.UTF8);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        return Process.Start(startInfo);
    }

    private void SetStartupEnabled(bool enabled, CheckBox? checkBox = null)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
            _appData.Settings.StartWithWindows = enabled;
            SaveData();
        }
        catch (Exception ex)
        {
            if (checkBox is not null) checkBox.IsChecked = StartupManager.IsEnabled();
            ShowNotice("Не удалось изменить автозагрузку", ex.Message);
        }
    }

    // ----------------------------- НАВИГАЦИЯ -----------------------------

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page) NavigateTo(page);
    }

    private void NavigateTo(string page)
    {
        _currentPage = page;
        // Секундомер и активный таймер продолжают идти, даже если пользователь
        // временно открыл другой раздел. В остальных случаях тикер не расходует ресурсы.
        if (page == "Clock" || _stopwatch.IsRunning || _countdownRunning) _clockTicker.Start();
        else _clockTicker.Stop();

        if (page == "Monitor") _monitorTicker.Start();
        else _monitorTicker.Stop();

        ContentArea.Content = page switch
        {
            "Clock" => BuildClockPage(),
            "Notes" => BuildNotesHome(),
            "Calculator" => BuildCalculatorPage(),
            "Converter" => BuildConverterPage(),
            "Reminders" => BuildRemindersPage(),
            "Pomodoro" => BuildPomodoroPage(),
            "Calendar" => BuildCalendarPage(),
            "Monitor" => BuildMonitorPage(),
            "Settings" => BuildSettingsPage(),
            _ => BuildClockPage()
        };
    }

    private void ShowAndNavigate(string page)
    {
        if (!IsVisible) RestoreFromTray();
        if (!_isExpanded) ExpandPanel();
        NavigateTo(page);
        Activate();
    }

    // ----------------------------- ЧАСЫ -----------------------------

    private UIElement BuildClockPage()
    {
        // Перестраиваем только элементы интерфейса. Состояние секундомера,
        // таймера и отсечек сохраняется при переходе между разделами.
        _stopwatchLapsText = null;
        _stopwatchLapsPanel = null;
        _stopwatchLapButton = null;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 16) };
        heading.Children.Add(MakeText("Часы", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText("Секундомер и таймер", 12, Brush("#AAB6CA")));
        root.Children.Add(heading);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _stopwatchModeButton = MakeButton("Секундомер", (_, _) => SetClockMode(false), "#22766F");
        _timerModeButton = MakeButton("Таймер", (_, _) => SetClockMode(true));
        _stopwatchModeButton.Width = 130;
        _timerModeButton.Width = 130;
        modeRow.Children.Add(_stopwatchModeButton);
        modeRow.Children.Add(_timerModeButton);
        content.Children.Add(modeRow);

        var clockCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18),
            Margin = new Thickness(0, 14, 0, 0)
        };
        Grid.SetRow(clockCard, 1);
        content.Children.Add(clockCard);

        var cardLayout = new Grid();
        cardLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        clockCard.Child = cardLayout;

        var centerArea = new Grid { Width = 250, Height = 250, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _timerTrack = new Ellipse { Stroke = Brush("#33445B"), StrokeThickness = 10, Visibility = Visibility.Collapsed };
        _timerArc = new ShapePath { Fill = Brushes.Transparent, Stroke = Brush("#64D8CB"), StrokeThickness = 10, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed };
        centerArea.Children.Add(_timerTrack);
        centerArea.Children.Add(_timerArc);

        var centerText = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _clockDisplay = MakeText("00:00.00", 37, Brush("#FFFFFF"), FontWeights.Light);
        _clockDisplay.TextAlignment = TextAlignment.Center;
        _clockCaption = MakeText("МИНУТЫ : СЕКУНДЫ . СОТЫЕ", 9, Brush("#91A1B9"));
        _clockCaption.TextAlignment = TextAlignment.Center;
        centerText.Children.Add(_clockDisplay);
        centerText.Children.Add(_clockCaption);
        centerArea.Children.Add(centerText);
        cardLayout.Children.Add(centerArea);

        _timerInputPanel = new Border { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        var timeInputs = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _hoursBox = MakeTimeBox("00");
        _minutesBox = MakeTimeBox("00");
        _secondsBox = MakeTimeBox("00");
        _hoursBox.TextChanged += TimerInputsChanged;
        _minutesBox.TextChanged += TimerInputsChanged;
        _secondsBox.TextChanged += TimerInputsChanged;
        _suppressTimerInput = true;
        _hoursBox.Text = Math.Max(0, (int)Math.Floor(_timerDuration.TotalHours)).ToString("00", CultureInfo.InvariantCulture);
        _minutesBox.Text = _timerDuration.Minutes.ToString("00", CultureInfo.InvariantCulture);
        _secondsBox.Text = _timerDuration.Seconds.ToString("00", CultureInfo.InvariantCulture);
        _suppressTimerInput = false;
        timeInputs.Children.Add(MakeTimeInput("Часы", _hoursBox));
        timeInputs.Children.Add(MakeTimeInput("Минуты", _minutesBox));
        timeInputs.Children.Add(MakeTimeInput("Секунды", _secondsBox));
        _timerInputPanel.Child = timeInputs;

        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
        _clockStartButton = MakeButton("Старт", ClockStartPause_Click, "#22766F");
        _clockStartButton.Width = 105;
        var resetButton = MakeButton("Сброс", ClockReset_Click);
        resetButton.Width = 92;
        _stopwatchLapButton = MakeButton("Отсечка", (_, _) => AddStopwatchLap());
        _stopwatchLapButton.Width = 92;
        controls.Children.Add(_clockStartButton);
        controls.Children.Add(_stopwatchLapButton);
        controls.Children.Add(resetButton);
        _stopwatchLapsText = MakeText("Отсечки появятся здесь", 10, Brush("#91A1B9"));
        _stopwatchLapsText.TextAlignment = TextAlignment.Center;
        _stopwatchLapsText.TextWrapping = TextWrapping.Wrap;
        _stopwatchLapsPanel = new Border
        {
            Background = Brush("#202F45"), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 7),
            Child = _stopwatchLapsText, Visibility = Visibility.Visible
        };
        var bottomControls = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        bottomControls.Children.Add(_stopwatchLapsPanel);
        bottomControls.Children.Add(_timerInputPanel);
        bottomControls.Children.Add(controls);
        Grid.SetRow(bottomControls, 1);
        cardLayout.Children.Add(bottomControls);

        _stopwatchModeButton.Background = Brush(_timerMode ? "#26364D" : "#22766F");
        _timerModeButton.Background = Brush(_timerMode ? "#22766F" : "#26364D");
        _timerTrack.Visibility = _timerMode ? Visibility.Visible : Visibility.Collapsed;
        _timerArc.Visibility = _timerMode ? Visibility.Visible : Visibility.Collapsed;
        _timerInputPanel.Visibility = _timerMode ? Visibility.Visible : Visibility.Collapsed;
        _stopwatchLapsPanel.Visibility = _timerMode ? Visibility.Collapsed : Visibility.Visible;
        _stopwatchLapButton.Visibility = _timerMode ? Visibility.Collapsed : Visibility.Visible;
        _clockCaption.Text = LocalizationService.Translate(_timerMode ? "ОСТАЛОСЬ ВРЕМЕНИ" : "МИНУТЫ : СЕКУНДЫ . СОТЫЕ");
        SetTimerInputsEnabled(!_countdownRunning);
        if (_timerMode)
            _clockStartButton.Content = LocalizationService.Translate(_countdownRunning ? "Пауза" : (_timerRemaining > TimeSpan.Zero && _timerRemaining < _timerDuration ? "Продолжить" : "Старт"));
        else
            _clockStartButton.Content = LocalizationService.Translate(_stopwatch.IsRunning ? "Пауза" : (_stopwatch.Elapsed > TimeSpan.Zero ? "Продолжить" : "Старт"));

        UpdateStopwatchLapsText();
        UpdateClockDisplay();
        _clockTicker.Start();
        return root;
    }

    private Border MakeTimeInput(string label, TextBox box)
    {
        var panel = new Border { Background = Brush("#26364D"), CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 6, 8, 5), Margin = new Thickness(5, 0, 5, 0) };
        var stack = new StackPanel();
        stack.Children.Add(MakeText(label, 10, Brush("#AAB6CA")));
        stack.Children.Add(box);
        panel.Child = stack;
        return panel;
    }

    private TextBox MakeTimeBox(string value)
    {
        var box = new TextBox
        {
            Text = value, Width = 58, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Brush("#FFFFFF"), FontSize = 18, TextAlignment = TextAlignment.Center,
            MaxLength = 2, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0)
        };
        box.PreviewTextInput += (_, e) => e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        return box;
    }

    private void TimerInputsChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressTimerInput || !_timerMode || _countdownRunning) return;
        _timerInputDirty = true;
        int hours = ParseTimeField(_hoursBox?.Text);
        int minutes = ParseTimeField(_minutesBox?.Text);
        int seconds = ParseTimeField(_secondsBox?.Text);
        if (hours >= 0 && minutes is >= 0 and <= 59 && seconds is >= 0 and <= 59)
        {
            _timerDuration = new TimeSpan(hours, minutes, seconds);
            _timerRemaining = _timerDuration;
        }
        else
        {
            _timerDuration = TimeSpan.Zero;
            _timerRemaining = TimeSpan.Zero;
        }
        UpdateClockDisplay();
    }

    private void SetClockMode(bool timerMode)
    {
        _timerMode = timerMode;
        if (_stopwatchModeButton is not null)
            _stopwatchModeButton.Background = Brush(timerMode ? "#26364D" : "#22766F");
        if (_timerModeButton is not null)
            _timerModeButton.Background = Brush(timerMode ? "#22766F" : "#26364D");
        _stopwatch.Stop();
        _stopwatch.Reset();
        _stopwatchLaps.Clear();
        _stopwatchLapNumber = 0;
        _lastStopwatchLapElapsed = TimeSpan.Zero;
        UpdateStopwatchLapsText();
        _countdownWatch.Stop();
        _countdownRunning = false;
        if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Старт");
        if (_timerTrack is not null) _timerTrack.Visibility = timerMode ? Visibility.Visible : Visibility.Collapsed;
        if (_timerArc is not null) _timerArc.Visibility = timerMode ? Visibility.Visible : Visibility.Collapsed;
        if (_timerInputPanel is not null) _timerInputPanel.Visibility = timerMode ? Visibility.Visible : Visibility.Collapsed;
        if (_stopwatchLapsPanel is not null) _stopwatchLapsPanel.Visibility = timerMode ? Visibility.Collapsed : Visibility.Visible;
        if (_stopwatchLapButton is not null) _stopwatchLapButton.Visibility = timerMode ? Visibility.Collapsed : Visibility.Visible;
        SetTimerInputsEnabled(true);
        if (_clockCaption is not null) _clockCaption.Text = LocalizationService.Translate(timerMode ? "ОСТАЛОСЬ ВРЕМЕНИ" : "МИНУТЫ : СЕКУНДЫ . СОТЫЕ");
        if (timerMode)
        {
            _timerDuration = TimeSpan.Zero;
            _timerRemaining = TimeSpan.Zero;
            _timerInputDirty = false;
            UpdateClockDisplay();
        }
        else UpdateClockDisplay();
    }

    private void ClockStartPause_Click(object? sender, RoutedEventArgs e)
    {
        if (!_timerMode)
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Продолжить");
            }
            else
            {
                _stopwatch.Start();
                if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Пауза");
            }
            return;
        }

        if (_countdownRunning)
        {
            _timerRemaining -= _countdownWatch.Elapsed;
            if (_timerRemaining < TimeSpan.Zero) _timerRemaining = TimeSpan.Zero;
            _countdownWatch.Reset();
            _countdownRunning = false;
            SetTimerInputsEnabled(true);
            if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Продолжить");
        }
        else
        {
            if (_timerInputDirty || _timerRemaining <= TimeSpan.Zero)
            {
                if (!ReadTimerInputs()) return;
            }
            if (_timerRemaining <= TimeSpan.Zero)
            {
                ShowNotice("Таймер", "Сначала установи время больше нуля.");
                return;
            }
            _countdownWatch.Restart();
            _countdownRunning = true;
            SetTimerInputsEnabled(false);
            if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Пауза");
        }
        UpdateClockDisplay();
    }

    private bool ReadTimerInputs()
    {
        int hours = ParseTimeField(_hoursBox?.Text);
        int minutes = ParseTimeField(_minutesBox?.Text);
        int seconds = ParseTimeField(_secondsBox?.Text);
        if (hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or > 59 || (hours + minutes + seconds) == 0)
        {
            ShowNotice("Неправильное время", "Укажи время больше нуля. Минуты и секунды должны быть от 00 до 59.");
            return false;
        }
        _timerDuration = new TimeSpan(hours, minutes, seconds);
        _timerRemaining = _timerDuration;
        _timerInputDirty = false;
        UpdateClockDisplay();
        return true;
    }

    private static int ParseTimeField(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private void ClockReset_Click(object? sender, RoutedEventArgs e)
    {
        _stopwatchLaps.Clear();
        _stopwatchLapNumber = 0;
        _lastStopwatchLapElapsed = TimeSpan.Zero;
        UpdateStopwatchLapsText();
        _stopwatch.Reset();
        _countdownWatch.Reset();
        _countdownRunning = false;
        if (_timerMode)
        {
            int hours = ParseTimeField(_hoursBox?.Text);
            int minutes = ParseTimeField(_minutesBox?.Text);
            int seconds = ParseTimeField(_secondsBox?.Text);
            _timerDuration = hours >= 0 && minutes is >= 0 and <= 59 && seconds is >= 0 and <= 59
                ? new TimeSpan(hours, minutes, seconds) : TimeSpan.Zero;
            _timerRemaining = _timerDuration;
            _timerInputDirty = false;
        }
        SetTimerInputsEnabled(true);
        if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Старт");
        UpdateClockDisplay();
    }

    private void AddStopwatchLap()
    {
        if (_timerMode || _stopwatch.Elapsed <= TimeSpan.Zero) return;
        _stopwatchLapNumber++;
        TimeSpan elapsed = _stopwatch.Elapsed;
        TimeSpan split = elapsed - _lastStopwatchLapElapsed;
        _lastStopwatchLapElapsed = elapsed;
        _stopwatchLaps.Insert(0, $"#{_stopwatchLapNumber:00}  +{FormatStopwatch(split)} / {FormatStopwatch(elapsed)}");
        if (_stopwatchLaps.Count > 8) _stopwatchLaps.RemoveAt(_stopwatchLaps.Count - 1);
        UpdateStopwatchLapsText();
    }

    private void UpdateStopwatchLapsText()
    {
        if (_stopwatchLapsText is null) return;
        _stopwatchLapsText.Text = _stopwatchLaps.Count == 0
            ? LocalizationService.Translate("Отсечки появятся здесь")
            : string.Join("     •     ", _stopwatchLaps);
    }

    private void SetTimerInputsEnabled(bool enabled)
    {
        if (_hoursBox is not null) _hoursBox.IsEnabled = enabled;
        if (_minutesBox is not null) _minutesBox.IsEnabled = enabled;
        if (_secondsBox is not null) _secondsBox.IsEnabled = enabled;
    }

    private void ClockTicker_Tick(object? sender, EventArgs e) => UpdateClockDisplay();

    private void UpdateClockDisplay()
    {
        if (_clockDisplay is null) return;
        TimeSpan shown = _timerMode
            ? (_countdownRunning ? _timerRemaining - _countdownWatch.Elapsed : _timerRemaining)
            : _stopwatch.Elapsed;

        if (_timerMode && shown <= TimeSpan.Zero && _countdownRunning)
        {
            shown = TimeSpan.Zero;
            _timerRemaining = TimeSpan.Zero;
            _countdownWatch.Stop();
            _countdownRunning = false;
            SetTimerInputsEnabled(true);
            if (_clockStartButton is not null) _clockStartButton.Content = LocalizationService.Translate("Старт");
            ShowReminderToast("Таймер завершён", "Время вышло. Можно начинать следующий отсчёт.");
        }
        if (shown < TimeSpan.Zero) shown = TimeSpan.Zero;
        _clockDisplay.Text = _timerMode ? FormatTimer(shown) : FormatStopwatch(shown);
        if (_timerMode && _timerDuration.TotalMilliseconds > 0)
        {
            double fraction = Math.Clamp(1 - shown.TotalMilliseconds / _timerDuration.TotalMilliseconds, 0, 1);
            UpdateTimerArc(fraction);
        }
        else if (_timerMode) UpdateTimerArc(0);

        if (_currentPage != "Clock" && !_stopwatch.IsRunning && !_countdownRunning)
            _clockTicker.Stop();
    }

    private static string FormatStopwatch(TimeSpan time)
    {
        long totalMinutes = (long)time.TotalMinutes;
        int hundredths = time.Milliseconds / 10;
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{hundredths:00}";
        return $"{totalMinutes:00}:{time.Seconds:00}.{hundredths:00}";
    }

    private static string FormatTimer(TimeSpan time)
    {
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    }

    private void UpdateTimerArc(double progress)
    {
        if (_timerArc is null) return;
        const double size = 250;
        const double center = size / 2;
        const double radius = 112;
        if (progress <= 0.001)
        {
            _timerArc.Data = Geometry.Empty;
            return;
        }
        if (progress >= 0.999)
        {
            _timerArc.Data = new EllipseGeometry(new Point(center, center), radius, radius);
            return;
        }
        double angle = -90 + 360 * progress;
        double radians = angle * Math.PI / 180;
        var end = new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center, center - radius), false, false);
            ctx.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        _timerArc.Data = geometry;
    }

    // ----------------------------- ЗАМЕТКИ -----------------------------

    private WrapPanel? _notesWrapPanel;
    private TextBlock? _notesEmptyText;

    private UIElement BuildNotesHome()
    {
        var root = new DockPanel();
        var top = new StackPanel { Margin = new Thickness(2, 0, 0, 12) };
        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(MakeText("Заметки", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        titles.Children.Add(MakeText("Поиск, категории и быстрый экспорт", 12, Brush("#AAB6CA")));
        titleRow.Children.Add(titles);
        var addButton = MakeButton("＋ Новый раздел", (_, _) => CreateNote(), "#22766F");
        Grid.SetColumn(addButton, 1);
        titleRow.Children.Add(addButton);
        top.Children.Add(titleRow);

        var searchRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new TextBox
        {
            Text = _noteSearchQuery, Height = 36, FontSize = 12, Padding = new Thickness(10, 7, 10, 7),
            Background = Brush("#192438"), Foreground = Brush("#FFFFFF"), BorderBrush = Brush("#354862"),
            BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = LocalizationService.Translate("Поиск по названию, тексту и категории")
        };
        search.TextChanged += (_, _) =>
        {
            _noteSearchQuery = search.Text;
            RefreshNoteCards();
        };
        searchRow.Children.Add(search);
        var clearSearch = MakeButton("×", (_, _) =>
        {
            search.Clear();
            search.Focus();
        });
        clearSearch.Width = 38; clearSearch.Height = 36; clearSearch.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(clearSearch, 1);
        searchRow.Children.Add(clearSearch);
        top.Children.Add(searchRow);
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var listHost = new Grid();
        _notesWrapPanel = new WrapPanel { Orientation = Orientation.Horizontal };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _notesWrapPanel
        };
        listHost.Children.Add(scroll);
        _notesEmptyText = MakeText("Заметки не найдены", 15, Brush("#AAB6CA"));
        _notesEmptyText.HorizontalAlignment = HorizontalAlignment.Center;
        _notesEmptyText.VerticalAlignment = VerticalAlignment.Center;
        _notesEmptyText.TextAlignment = TextAlignment.Center;
        _notesEmptyText.Visibility = Visibility.Collapsed;
        listHost.Children.Add(_notesEmptyText);
        root.Children.Add(listHost);
        RefreshNoteCards();
        return root;
    }

    private void RefreshNoteCards()
    {
        if (_notesWrapPanel is null) return;
        _notesWrapPanel.Children.Clear();
        string query = (_noteSearchQuery ?? string.Empty).Trim();
        List<NoteItem> filtered = _appData.Notes
            .Where(note => query.Length == 0 ||
                note.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                note.Content.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                note.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(note => note.IsPinned)
            .ThenByDescending(note => note.UpdatedAt)
            .ToList();

        foreach (NoteItem note in filtered)
        {
            var card = new Border
            {
                Width = 166, Height = 132, Margin = new Thickness(0, 0, 12, 12), Padding = new Thickness(13),
                Background = Brush(note.Color), BorderBrush = Brush("#354862"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, Tag = note.Id
            };
            var cardStack = new StackPanel();
            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(MakeText("▤", 19, Brush("#64D8CB")));
            if (note.IsPinned)
            {
                var pin = MakeText("◆", 10, Brush("#64D8CB"));
                Grid.SetColumn(pin, 1);
                header.Children.Add(pin);
            }
            cardStack.Children.Add(header);
            cardStack.Children.Add(MakeText(note.Title, 13, Brush("#FFFFFF"), FontWeights.SemiBold, 1, translate: false));
            if (!string.IsNullOrWhiteSpace(note.Category))
                cardStack.Children.Add(MakeText(note.Category, 9, Brush("#64D8CB"), FontWeights.Medium, 1, translate: false));
            cardStack.Children.Add(MakeText(note.Content.Replace('\n', ' '), 10, Brush("#AAB6CA"), FontWeights.Normal, 2, translate: false));
            card.Child = cardStack;
            card.MouseLeftButtonUp += (_, _) => OpenNote(note);

            var menu = new ContextMenu();
            var pinItem = new MenuItem { Header = LocalizationService.Translate(note.IsPinned ? "Открепить" : "Закрепить сверху") };
            pinItem.Click += (_, _) =>
            {
                note.IsPinned = !note.IsPinned;
                note.UpdatedAt = DateTime.Now;
                SaveData();
                RefreshNoteCards();
            };
            menu.Items.Add(pinItem);
            var exportItem = new MenuItem { Header = LocalizationService.Translate("Экспортировать…") };
            exportItem.Click += (_, _) => ExportNote(note);
            menu.Items.Add(exportItem);
            var delete = new MenuItem { Header = LocalizationService.Translate("Удалить раздел") };
            delete.Click += (_, _) => DeleteNote(note);
            menu.Items.Add(delete);
            card.ContextMenu = menu;
            _notesWrapPanel.Children.Add(card);
        }

        if (_notesEmptyText is not null)
        {
            _notesEmptyText.Text = _appData.Notes.Count == 0
                ? LocalizationService.Translate("Пока здесь пусто") + Environment.NewLine + LocalizationService.Translate("Нажми «Новый раздел», чтобы создать первую заметку.")
                : LocalizationService.Translate("По запросу ничего не найдено");
            _notesEmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CreateNote()
    {
        var note = new NoteItem { Title = "Новая заметка", Content = string.Empty, UpdatedAt = DateTime.Now };
        _appData.Notes.Add(note);
        SaveData();
        ContentArea.Content = BuildNoteEditor(note);
    }

    private void OpenNote(NoteItem note) => ContentArea.Content = BuildNoteEditor(note);

    private UIElement BuildNoteEditor(NoteItem note)
    {
        _notesWrapPanel = null;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var back = MakeButton("←", (_, _) => ContentArea.Content = BuildNotesHome());
        back.Width = 42; back.Margin = new Thickness(0);
        top.Children.Add(back);
        var title = new TextBox
        {
            Text = note.Title, FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brush("#FFFFFF"),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(12, 5, 5, 5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        title.TextChanged += (_, _) =>
        {
            note.Title = string.IsNullOrWhiteSpace(title.Text) ? "Без названия" : title.Text;
            note.UpdatedAt = DateTime.Now;
            SaveData();
        };
        Grid.SetColumn(title, 1);
        top.Children.Add(title);
        var export = MakeButton("Экспорт", (_, _) => ExportNote(note));
        export.Margin = new Thickness(5, 0, 0, 0);
        Grid.SetColumn(export, 2);
        top.Children.Add(export);
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var meta = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(138) });
        meta.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var category = new TextBox
        {
            Text = note.Category, Height = 33, Padding = new Thickness(8, 5, 8, 5), FontSize = 11,
            Background = Brush("#192438"), Foreground = Brush("#FFFFFF"), BorderBrush = Brush("#354862"),
            ToolTip = LocalizationService.Translate("Категория или тег, например «Работа»")
        };
        category.TextChanged += (_, _) => { note.Category = category.Text.Trim(); SaveData(); };
        meta.Children.Add(category);
        string[] colorNames = { "Синяя", "Фиолетовая", "Зелёная", "Бордовая", "Графитовая", "Свой цвет…" };
        string[] colorValues = { "#1B2A40", "#342D4B", "#233F3A", "#493039", "#303947" };
        var colorCombo = MakeCombo(colorNames);
        colorCombo.Margin = new Thickness(6, 0, 0, 0);
        int colorIndex = Array.IndexOf(colorValues, note.Color);
        int lastPresetIndex = colorIndex >= 0 ? colorIndex : 0;
        colorCombo.SelectedIndex = colorIndex < 0 ? colorNames.Length - 1 : colorIndex;
        colorCombo.SelectionChanged += (_, _) =>
        {
            int selectedIndex = colorCombo.SelectedIndex;
            if (selectedIndex >= 0 && selectedIndex < colorValues.Length)
            {
                lastPresetIndex = selectedIndex;
                note.Color = colorValues[selectedIndex];
                SaveData();
                return;
            }
            if (selectedIndex != colorValues.Length) return;

            Color currentColor = Colors.SlateBlue;
            try { currentColor = (Color)ColorConverter.ConvertFromString(note.Color); } catch { }
            using var colorDialog = new Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = Drawing.Color.FromArgb(currentColor.R, currentColor.G, currentColor.B)
            };
            if (colorDialog.ShowDialog() == Forms.DialogResult.OK)
            {
                note.Color = $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
                SaveData();
            }
            else
            {
                colorCombo.SelectedIndex = lastPresetIndex;
            }
        };
        Grid.SetColumn(colorCombo, 1);
        meta.Children.Add(colorCombo);
        var pinned = new CheckBox
        {
            Content = LocalizationService.Translate("Закрепить"), IsChecked = note.IsPinned, Foreground = Brush("#AAB6CA"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), FontSize = 11
        };
        pinned.Checked += (_, _) => { note.IsPinned = true; SaveData(); };
        pinned.Unchecked += (_, _) => { note.IsPinned = false; SaveData(); };
        Grid.SetColumn(pinned, 2);
        meta.Children.Add(pinned);
        Grid.SetRow(meta, 1);
        root.Children.Add(meta);

        var body = new TextBox
        {
            Text = note.Content, AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontSize = 14, Foreground = Brush("#EAF0F8"), Background = Brush("#192438"),
            BorderBrush = Brush("#354862"), BorderThickness = new Thickness(1), Padding = new Thickness(16),
            CaretBrush = Brush("#64D8CB")
        };
        body.TextChanged += (_, _) =>
        {
            note.Content = body.Text;
            note.UpdatedAt = DateTime.Now;
            SaveData();
        };
        Grid.SetRow(body, 3);
        root.Children.Add(body);
        return root;
    }

    private void ExportNote(NoteItem note)
    {
        using var dialog = new Forms.SaveFileDialog
        {
            Title = LocalizationService.Translate("Экспорт заметки"),
            FileName = string.Concat(note.Title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)),
            Filter = $"Markdown (*.md)|*.md|{LocalizationService.Translate("Текстовые файлы")} (*.txt)|*.txt",
            DefaultExt = ".md",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try
        {
            string content = dialog.FilterIndex == 1
                ? $"# {note.Title}\n\n" + (string.IsNullOrWhiteSpace(note.Category) ? string.Empty : $"Категория: {note.Category}\n\n") + note.Content
                : note.Content;
            File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
            string exportMessage = LocalizationService.Translate("Заметка сохранена: {path}")
                .Replace("{path}", dialog.FileName, StringComparison.Ordinal);
            ShowNotice("Экспорт завершён", exportMessage);
        }
        catch (Exception ex) { ShowNotice("Не удалось экспортировать заметку", ex.Message); }
    }

    private void DeleteNote(NoteItem note)
    {
        ShowConfirm("Удаление раздела", LocalizationService.Translate("Удалить раздел «{title}»? Это действие нельзя отменить.")
            .Replace("{title}", note.Title, StringComparison.Ordinal), () =>
        {
            _appData.Notes.RemoveAll(n => n.Id == note.Id);
            SaveData();
            ContentArea.Content = BuildNotesHome();
        });
    }

    // ----------------------------- КАЛЬКУЛЯТОР -----------------------------

    private UIElement BuildCalculatorPage()
    {
        _expressionEvaluator.UseDegrees = _degreeMode;
        _trigButtons.Clear();
        _degreeButton = null;
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel();
        heading.Children.Add(MakeText("Калькулятор", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        root.Children.Add(heading);

        var displayCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0, 14, 0, 10) };
        _calculatorDisplay = new TextBox
        {
            Text = "0", FontSize = 31, Foreground = Brush("#FFFFFF"), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), TextAlignment = TextAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(5), CaretBrush = Brush("#64D8CB"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _calculatorDisplay.PreviewKeyDown += CalculatorDisplay_PreviewKeyDown;
        displayCard.Child = _calculatorDisplay;
        Grid.SetRow(displayCard, 1);
        root.Children.Add(displayCard);

        var historyRow = new Grid { Margin = new Thickness(0, 0, 0, 7) };
        historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        historyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _calculatorHistoryText = MakeText(string.Empty, 9, Brush("#91A1B9"));
        _calculatorHistoryText.TextWrapping = TextWrapping.NoWrap;
        _calculatorHistoryText.TextTrimming = TextTrimming.CharacterEllipsis;
        historyRow.Children.Add(_calculatorHistoryText);
        var historyButton = MakeButton("История", (_, _) => ShowCalculatorHistory());
        historyButton.Height = 27; historyButton.Margin = new Thickness(6, 0, 0, 0); historyButton.Padding = new Thickness(9, 2, 9, 2);
        Grid.SetColumn(historyButton, 1);
        historyRow.Children.Add(historyButton);
        Grid.SetRow(historyRow, 2);
        root.Children.Add(historyRow);
        UpdateCalculatorHistoryText();

        string[,] keys =
        {
            { "MC", "MR", "M+", "M−", "HIST" },
            { "2nd", "deg", "sin", "cos", "tan" },
            { "xʸ", "lg", "ln", "(", ")" },
            { "√x", "AC", "⌫", "%", "÷" },
            { "x!", "7", "8", "9", "×" },
            { "1/x", "4", "5", "6", "−" },
            { "π", "1", "2", "3", "+" },
            { "e", "0", ".", "±", "=" }
        };
        var grid = new UniformGrid { Columns = 5, Rows = 8, Margin = new Thickness(0, 0, 0, 0) };
        for (int r = 0; r < keys.GetLength(0); r++)
        {
            for (int c = 0; c < keys.GetLength(1); c++)
            {
                string key = keys[r, c];
                bool isOperator = key is "÷" or "×" or "−" or "+" or "xʸ" or "%" or "√x" or "=";
                string background = key == "=" ? "#22766F" : isOperator ? "#30435C" : key is "MC" or "MR" or "M+" or "M−" or "HIST" ? "#26364D" : "#202F45";
                var button = MakeButton(key, (_, _) => CalculatorKey(key), background);
                button.FontSize = key.Length > 3 ? 13 : 16;
                button.Margin = new Thickness(3);
                button.Height = 34;
                button.Padding = new Thickness(3, 3, 3, 3);
                if (key == "deg") _degreeButton = button;
                if (key is "sin" or "cos" or "tan") _trigButtons.Add(button);
                grid.Children.Add(button);
            }
        }
        Grid.SetRow(grid, 3);
        root.Children.Add(grid);
        UpdateCalculatorModeButtons();
        return root;
    }

    private void CalculatorDisplay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CalculatorKey("=");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CalculatorKey("AC");
            e.Handled = true;
        }
    }

    private void CalculatorKey(string key)
    {
        if (_calculatorDisplay is null) return;
        string current = _calculatorDisplay.Text == "0" ? string.Empty : _calculatorDisplay.Text;
        switch (key)
        {
            case "AC":
                _calculatorDisplay.Text = "0";
                break;
            case "MC":
                _appData.Settings.CalculatorMemory = 0;
                SaveData();
                break;
            case "MR":
                _calculatorDisplay.Text = _appData.Settings.CalculatorMemory.ToString("G12", CultureInfo.InvariantCulture);
                break;
            case "M+":
            case "M−":
                if (TryGetCalculatorValue(out double memoryValue))
                {
                    _appData.Settings.CalculatorMemory += key == "M+" ? memoryValue : -memoryValue;
                    SaveData();
                    string memoryNotice = LocalizationService.Translate("В памяти: {value}")
                        .Replace("{value}", _appData.Settings.CalculatorMemory.ToString("G12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
                    ShowNotice("Память калькулятора", memoryNotice);
                }
                break;
            case "HIST":
                ShowCalculatorHistory();
                break;
            case "⌫":
                if (_calculatorDisplay.SelectionLength > 0) _calculatorDisplay.SelectedText = string.Empty;
                else if (_calculatorDisplay.CaretIndex > 0)
                {
                    int caret = _calculatorDisplay.CaretIndex;
                    _calculatorDisplay.Text = _calculatorDisplay.Text.Remove(caret - 1, 1);
                    _calculatorDisplay.CaretIndex = caret - 1;
                }
                else if (current.Length > 0) _calculatorDisplay.Text = current[..^1];
                if (string.IsNullOrWhiteSpace(_calculatorDisplay.Text)) _calculatorDisplay.Text = "0";
                break;
            case "=":
                try
                {
                    string expression = _calculatorDisplay.Text;
                    double result = _expressionEvaluator.Evaluate(expression);
                    string resultText = result.ToString("G12", CultureInfo.InvariantCulture);
                    _appData.Settings.CalculatorHistory.Insert(0, $"{expression} = {resultText}");
                    if (_appData.Settings.CalculatorHistory.Count > 50)
                        _appData.Settings.CalculatorHistory.RemoveRange(50, _appData.Settings.CalculatorHistory.Count - 50);
                    _calculatorDisplay.Text = resultText;
                    _calculatorDisplay.CaretIndex = _calculatorDisplay.Text.Length;
                    SaveData();
                    UpdateCalculatorHistoryText();
                }
                catch (Exception ex) { ShowNotice("Калькулятор", ex.Message); }
                break;
            case "2nd":
                _secondMode = !_secondMode;
                UpdateCalculatorModeButtons();
                break;
            case "deg":
                _degreeMode = !_degreeMode;
                _expressionEvaluator.UseDegrees = _degreeMode;
                UpdateCalculatorModeButtons();
                break;
            case "±":
                _calculatorDisplay.Text = string.IsNullOrWhiteSpace(current) ? "-" : $"-({current})";
                break;
            case "√x":
                _calculatorDisplay.Text = string.IsNullOrWhiteSpace(current) ? "sqrt(" : $"sqrt({current})";
                break;
            case "1/x":
                _calculatorDisplay.Text = string.IsNullOrWhiteSpace(current) ? "1/(" : $"1/({current})";
                break;
            case "x!":
                _calculatorDisplay.Text = (string.IsNullOrWhiteSpace(current) ? "0" : current) + "!";
                break;
            case "xʸ": _calculatorDisplay.Text = current + "^"; break;
            case "π": _calculatorDisplay.Text = current + "pi"; break;
            case "e": _calculatorDisplay.Text = current + "e"; break;
            case "sin":
            case "cos":
            case "tan":
            case "lg":
            case "ln":
                string function = key;
                if (_secondMode && (key is "sin" or "cos" or "tan")) function = key switch { "sin" => "asin", "cos" => "acos", _ => "atan" };
                _calculatorDisplay.Text = string.IsNullOrWhiteSpace(current) ? function + "(" : function + "(" + current + ")";
                break;
            case "×": _calculatorDisplay.Text = current + "*"; break;
            case "÷": _calculatorDisplay.Text = current + "/"; break;
            case "−": _calculatorDisplay.Text = current + "-"; break;
            default:
                _calculatorDisplay.Text = current + key;
                break;
        }
        _calculatorDisplay.CaretIndex = _calculatorDisplay.Text.Length;
        UpdateCalculatorHistoryText();
    }

    private bool TryGetCalculatorValue(out double value)
    {
        value = 0;
        if (_calculatorDisplay is null) return false;
        try
        {
            value = _expressionEvaluator.Evaluate(_calculatorDisplay.Text);
            return double.IsFinite(value);
        }
        catch (Exception ex)
        {
            ShowNotice("Калькулятор", ex.Message);
            return false;
        }
    }

    private void UpdateCalculatorHistoryText()
    {
        if (_calculatorHistoryText is null) return;
        string memoryValue = _appData.Settings.CalculatorMemory.ToString("G6", CultureInfo.InvariantCulture);
        string historyText = _appData.Settings.CalculatorHistory.Count == 0
            ? LocalizationService.Translate("Память: {memory} • История пока пуста").Replace("{memory}", memoryValue, StringComparison.Ordinal)
            : LocalizationService.Translate("Память: {memory} • {history}")
                .Replace("{memory}", memoryValue, StringComparison.Ordinal)
                .Replace("{history}", string.Join("   |   ", _appData.Settings.CalculatorHistory.Take(2)), StringComparison.Ordinal);
        _calculatorHistoryText.Text = historyText;
    }

    private void ShowCalculatorHistory()
    {
        List<string> history = _appData.Settings.CalculatorHistory.Take(15).ToList();
        string message = history.Count == 0 ? "История вычислений пока пуста." : string.Join(Environment.NewLine, history);
        ShowTwoChoice("История вычислений", message, "Очистить", () =>
        {
            _appData.Settings.CalculatorHistory.Clear();
            SaveData();
            UpdateCalculatorHistoryText();
        }, "Закрыть", null);
    }

    private void UpdateCalculatorModeButtons()
    {
        if (_degreeButton is not null) _degreeButton.Content = _degreeMode ? "DEG" : "RAD";
        if (_trigButtons.Count == 3)
        {
            _trigButtons[0].Content = _secondMode ? "sin⁻¹" : "sin";
            _trigButtons[1].Content = _secondMode ? "cos⁻¹" : "cos";
            _trigButtons[2].Content = _secondMode ? "tan⁻¹" : "tan";
        }
    }

    // ----------------------------- КОНВЕРТЕР -----------------------------

    private sealed record UnitDef(string Name, double Factor = 1, string? CurrencyCode = null);
    private sealed record CategoryDef(string Name, List<UnitDef> Units);

    private static readonly List<CategoryDef> ConverterCategories = new()
    {
        new("Длина", new() { new("Метры (м)",1), new("Километры (км)",1000), new("Сантиметры (см)",0.01), new("Миллиметры (мм)",0.001), new("Мили",1609.344), new("Футы",0.3048), new("Дюймы",0.0254) }),
        new("Масса", new() { new("Килограммы (кг)",1), new("Граммы (г)",0.001), new("Миллиграммы (мг)",0.000001), new("Тонны",1000), new("Фунты",0.45359237), new("Унции",0.028349523125) }),
        new("Время", new() { new("Секунды (с)",1), new("Миллисекунды (мс)",0.001), new("Минуты",60), new("Часы",3600), new("Сутки",86400), new("Недели",604800) }),
        new("Площадь", new() { new("Квадратные метры",1), new("Квадратные километры",1_000_000), new("Квадратные сантиметры",0.0001), new("Гектары",10000), new("Акры",4046.8564224) }),
        new("Объём", new() { new("Кубические метры",1), new("Литры",0.001), new("Миллилитры",0.000001), new("Кубические сантиметры",0.000001), new("Галлоны (US)",0.003785411784) }),
        new("Скорость", new() { new("Метры в секунду",1), new("Километры в час",1.0/3.6), new("Мили в час",0.44704), new("Узлы",0.514444444) }),
        new("Энергия", new() { new("Джоули (Дж)",1), new("Килоджоули (кДж)",1000), new("Калории",4.184), new("Килокалории",4184), new("Ватт-часы",3600), new("Киловатт-часы",3_600_000), new("Электронвольты",1.602176634e-19) }),
        new("Давление", new() { new("Паскали (Па)",1), new("Килопаскали (кПа)",1000), new("Бары",100000), new("Атмосферы",101325), new("Миллиметры рт. ст.",133.322368) }),
        new("Мощность", new() { new("Ватты (Вт)",1), new("Киловатты (кВт)",1000), new("Мегаватты (МВт)",1_000_000), new("Лошадиные силы",745.699872) }),
        new("Цифровые данные", new() { new("Байты",1), new("Килобайты (KB)",1000), new("Мегабайты (MB)",1_000_000), new("Гигабайты (GB)",1_000_000_000), new("Кибибайты (KiB)",1024), new("Мебибайты (MiB)",1_048_576), new("Гибибайты (GiB)",1_073_741_824) }),
        new("Температура", new() { new("Цельсий (°C)",1), new("Фаренгейт (°F)",1), new("Кельвин (K)",1) }),
        new("Частота", new() { new("Герцы (Hz)",1), new("Килогерцы (kHz)",1_000), new("Мегагерцы (MHz)",1_000_000), new("Гигагерцы (GHz)",1_000_000_000) }),
        new("Сила", new() { new("Ньютоны (N)",1), new("Килоньютоны (kN)",1_000), new("Дин (dyn)",0.00001), new("Килограмм-сила (kgf)",9.80665) }),
        new("Электрический ток", new() { new("Амперы (A)",1), new("Миллиамперы (mA)",0.001), new("Микроамперы (µA)",0.000001), new("Килоамперы (kA)",1_000) }),
        new("Напряжение", new() { new("Вольты (V)",1), new("Милливольты (mV)",0.001), new("Микровольты (µV)",0.000001), new("Киловольты (kV)",1_000) }),
        new("Расход данных", new() { new("Бит/с (bps)",1), new("Кбит/с (kbps)",1_000), new("Мбит/с (Mbps)",1_000_000), new("Гбит/с (Gbps)",1_000_000_000), new("Байт/с (B/s)",8), new("Мбайт/с (MB/s)",8_000_000) }),
        new("Валюта", new() {
            new("Евро (EUR)",1,"EUR"), new("Доллар США (USD)",1,"USD"), new("Фунт стерлингов (GBP)",1,"GBP"),
            new("Японская иена (JPY)",1,"JPY"), new("Швейцарский франк (CHF)",1,"CHF"), new("Канадский доллар (CAD)",1,"CAD"),
            new("Австралийский доллар (AUD)",1,"AUD"), new("Польский злотый (PLN)",1,"PLN"), new("Чешская крона (CZK)",1,"CZK"),
            new("Шведская крона (SEK)",1,"SEK"), new("Норвежская крона (NOK)",1,"NOK"), new("Датская крона (DKK)",1,"DKK"),
            new("Китайский юань (CNY)",1,"CNY"), new("Индийская рупия (INR)",1,"INR"), new("Бразильский реал (BRL)",1,"BRL"),
            new("Мексиканское песо (MXN)",1,"MXN"), new("Новозеландский доллар (NZD)",1,"NZD"), new("Сингапурский доллар (SGD)",1,"SGD"),
            new("Южноафриканский рэнд (ZAR)",1,"ZAR"), new("Турецкая лира (TRY)",1,"TRY")
        })
    };

    private UIElement BuildConverterPage()
    {
        var root = new StackPanel();
        root.Children.Add(MakeText("Конвертер величин", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        root.Children.Add(MakeText("Переводи единицы измерения физических величин", 12, Brush("#AAB6CA")));

        var card = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(16), Padding = new Thickness(18), Margin = new Thickness(0, 18, 0, 0) };
        var layout = new StackPanel();
        layout.Children.Add(MakeText("Категория", 12, Brush("#AAB6CA")));
        _categoryCombo = MakeCombo(ConverterCategories.Select(c => c.Name));
        _categoryCombo.SelectionChanged += (_, _) => UpdateConverterUnits();
        layout.Children.Add(_categoryCombo);

        layout.Children.Add(MakeText("Из", 12, Brush("#AAB6CA")));
        _fromUnitCombo = MakeCombo(Array.Empty<string>());
        _fromUnitCombo.SelectionChanged += (_, _) => CalculateConversion();
        layout.Children.Add(_fromUnitCombo);

        layout.Children.Add(MakeText("В", 12, Brush("#AAB6CA")));
        _toUnitCombo = MakeCombo(Array.Empty<string>());
        _toUnitCombo.SelectionChanged += (_, _) => CalculateConversion();
        layout.Children.Add(_toUnitCombo);

        layout.Children.Add(MakeText("Значение", 12, Brush("#AAB6CA")));
        _converterInput = new TextBox
        {
            Text = "1", FontSize = 19, Foreground = Brush("#FFFFFF"), Background = Brush("#26364D"),
            BorderThickness = new Thickness(0), Padding = new Thickness(12), Margin = new Thickness(0, 3, 0, 10)
        };
        _converterInput.TextChanged += (_, _) => CalculateConversion();
        layout.Children.Add(_converterInput);

        _currencyActionPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
        _currencyRefreshButton = MakeButton("Обновить курс и рассчитать", async (_, _) => await CalculateCurrencyConversionAsync(), "#22766F");
        _currencyRefreshButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _currencyActionPanel.Children.Add(_currencyRefreshButton);
        _currencyStatus = MakeText("Курсы валют загружаются по запросу из интернета.", 9, Brush("#91A1B9"));
        _currencyStatus.TextWrapping = TextWrapping.Wrap;
        _currencyStatus.Margin = new Thickness(0, 4, 0, 0);
        _currencyActionPanel.Children.Add(_currencyStatus);
        layout.Children.Add(_currencyActionPanel);

        var resultCard = new Border { Background = Brush("#243B4D"), CornerRadius = new CornerRadius(12), Padding = new Thickness(14), Margin = new Thickness(0, 8, 0, 0) };
        var resultStack = new StackPanel();
        resultStack.Children.Add(MakeText("РЕЗУЛЬТАТ", 10, Brush("#91A1B9")));
        _converterResult = MakeText("—", 24, Brush("#64D8CB"), FontWeights.SemiBold);
        resultStack.Children.Add(_converterResult);
        resultCard.Child = resultStack;
        layout.Children.Add(resultCard);
        card.Child = layout;
        root.Children.Add(card);

        _categoryCombo.SelectedIndex = 0;
        UpdateConverterUnits();
        return root;
    }

    private ComboBox MakeCombo(IEnumerable<string> items)
    {
        var itemTextFactory = new FrameworkElementFactory(typeof(TextBlock));
        itemTextFactory.SetBinding(TextBlock.TextProperty, new Binding { Converter = new LocalizedStringConverter() });
        var itemTemplate = new DataTemplate { VisualTree = itemTextFactory };
        var combo = new ComboBox
        {
            ItemsSource = items.ToList(), SelectedIndex = 0, FontSize = 13, Foreground = Brush("#FFFFFF"),
            Background = Brush("#26364D"), BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 3, 0, 10), Height = 38,
            ItemTemplate = itemTemplate
        };
        return combo;
    }

    private void UpdateConverterUnits()
    {
        ComboBox? categoryCombo = _categoryCombo;
        ComboBox? fromUnitCombo = _fromUnitCombo;
        ComboBox? toUnitCombo = _toUnitCombo;
        if (categoryCombo is null || fromUnitCombo is null || toUnitCombo is null) return;

        int index = categoryCombo.SelectedIndex;
        if ((uint)index >= (uint)ConverterCategories.Count) return;
        CategoryDef selectedCategory = ConverterCategories[index];
        bool isCurrency = selectedCategory.Name == "Валюта";
        if (_currencyActionPanel is not null) _currencyActionPanel.Visibility = isCurrency ? Visibility.Visible : Visibility.Collapsed;
        Interlocked.Increment(ref _currencyRequestId);
        List<string> names = selectedCategory.Units.Select(u => u.Name).ToList();
        _updatingConverter = true;
        fromUnitCombo.ItemsSource = names;
        toUnitCombo.ItemsSource = names;
        fromUnitCombo.SelectedIndex = 0;
        toUnitCombo.SelectedIndex = Math.Min(1, names.Count - 1);
        _updatingConverter = false;
        CalculateConversion();
    }

    private void CalculateConversion()
    {
        ComboBox? categoryCombo = _categoryCombo;
        ComboBox? fromUnitCombo = _fromUnitCombo;
        ComboBox? toUnitCombo = _toUnitCombo;
        TextBox? converterInput = _converterInput;
        TextBlock? converterResult = _converterResult;
        if (_updatingConverter || categoryCombo is null || fromUnitCombo is null || toUnitCombo is null ||
            converterInput is null || converterResult is null)
            return;

        int categoryIndex = categoryCombo.SelectedIndex;
        int fromIndex = fromUnitCombo.SelectedIndex;
        int toIndex = toUnitCombo.SelectedIndex;
        if ((uint)categoryIndex >= (uint)ConverterCategories.Count) return;
        CategoryDef category = ConverterCategories[categoryIndex];
        if ((uint)fromIndex >= (uint)category.Units.Count || (uint)toIndex >= (uint)category.Units.Count) return;

        if (!double.TryParse(converterInput.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            converterResult.Text = LocalizationService.Translate("Проверь ввод");
            return;
        }

        UnitDef from = category.Units[fromIndex];
        UnitDef to = category.Units[toIndex];
        double baseValue;
        double converted;

        if (category.Name == "Валюта")
        {
            if (from.CurrencyCode == to.CurrencyCode)
            {
                converterResult.Text = value.ToString("G10", CultureInfo.InvariantCulture);
                if (_currencyStatus is not null) _currencyStatus.Text = LocalizationService.Translate("Одинаковая валюта — курс равен 1.");
            }
            else
            {
                converterResult.Text = LocalizationService.Translate("Нажми «Обновить курс»");
                if (_currencyStatus is not null) _currencyStatus.Text = LocalizationService.Translate("Курсы справочные; для обновления требуется подключение к интернету.");
            }
            return;
        }

        if (category.Name == "Температура")
        {
            baseValue = from.Name.StartsWith("Фаренгейт", StringComparison.Ordinal) ? (value - 32) * 5 / 9 + 273.15
                : from.Name.StartsWith("Кельвин", StringComparison.Ordinal) ? value : value + 273.15;
            converted = to.Name.StartsWith("Фаренгейт", StringComparison.Ordinal) ? (baseValue - 273.15) * 9 / 5 + 32
                : to.Name.StartsWith("Кельвин", StringComparison.Ordinal) ? baseValue : baseValue - 273.15;
        }
        else
        {
            baseValue = value * from.Factor;
            converted = baseValue / to.Factor;
        }

        if (double.IsNaN(converted) || double.IsInfinity(converted)) converterResult.Text = LocalizationService.Translate("Не удалось вычислить");
        else converterResult.Text = converted.ToString("G10", CultureInfo.InvariantCulture);
    }

    private async Task CalculateCurrencyConversionAsync()
    {
        ComboBox? categoryCombo = _categoryCombo;
        ComboBox? fromUnitCombo = _fromUnitCombo;
        ComboBox? toUnitCombo = _toUnitCombo;
        TextBox? converterInput = _converterInput;
        TextBlock? converterResult = _converterResult;
        Button? currencyRefreshButton = _currencyRefreshButton;
        if (categoryCombo is null || fromUnitCombo is null || toUnitCombo is null ||
            converterInput is null || converterResult is null || currencyRefreshButton is null) return;

        int categoryIndex = categoryCombo.SelectedIndex;
        int fromIndex = fromUnitCombo.SelectedIndex;
        int toIndex = toUnitCombo.SelectedIndex;
        if ((uint)categoryIndex >= (uint)ConverterCategories.Count) return;
        CategoryDef category = ConverterCategories[categoryIndex];
        if (category.Name != "Валюта") return;
        if ((uint)fromIndex >= (uint)category.Units.Count || (uint)toIndex >= (uint)category.Units.Count) return;
        UnitDef from = category.Units[fromIndex];
        UnitDef to = category.Units[toIndex];
        if (from.CurrencyCode is null || to.CurrencyCode is null) return;
        if (!decimal.TryParse(converterInput.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount))
        {
            converterResult.Text = LocalizationService.Translate("Проверь ввод");
            return;
        }
        if (from.CurrencyCode == to.CurrencyCode)
        {
            converterResult.Text = amount.ToString("G12", CultureInfo.InvariantCulture);
            if (_currencyStatus is not null) _currencyStatus.Text = LocalizationService.Translate("Одинаковая валюта — курс равен 1.");
            return;
        }

        int requestId = Interlocked.Increment(ref _currencyRequestId);
        currencyRefreshButton.IsEnabled = false;
        if (_currencyStatus is not null) _currencyStatus.Text = LocalizationService.Translate("Получаю последний доступный справочный курс…");
        try
        {
            CurrencyRate rate = await _currencyRateService.GetRateAsync(from.CurrencyCode, to.CurrencyCode);
            if (requestId != _currencyRequestId || _currentPage != "Converter") return;
            decimal result = amount * rate.Rate;
            converterResult.Text = result.ToString("N4", LocalizationService.CurrentCulture);
            if (_currencyStatus is not null)
                _currencyStatus.Text = LocalizationService.Translate($"1 {from.CurrencyCode} = {rate.Rate.ToString("G8", CultureInfo.InvariantCulture)} {to.CurrencyCode} · дата курса: {rate.Date:dd.MM.yyyy}. Источник: Frankfurter.");
        }
        catch (Exception ex)
        {
            if (requestId == _currencyRequestId && _currencyStatus is not null)
            {
                converterResult.Text = LocalizationService.Translate("Курс недоступен");
                _currencyStatus.Text = LocalizationService.Translate("Не удалось получить курс. Проверь интернет и попробуй ещё раз.") + " " + ex.Message;
            }
        }
        finally
        {
            if (requestId == _currencyRequestId && _currencyRefreshButton is not null)
                currencyRefreshButton.IsEnabled = true;
        }
    }

    // ----------------------------- НАПОМИНАНИЯ -----------------------------

    private UIElement BuildRemindersPage()
    {
        var root = new DockPanel();
        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(MakeText("Напоминания", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText("Ежедневно, по выбранным дням или без повтора", 12, Brush("#AAB6CA")));
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var addCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14) };
        var form = new Grid();
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var textStack = new StackPanel();
        textStack.Children.Add(MakeText("Напоминание", 11, Brush("#AAB6CA")));
        _reminderTextInput = new TextBox { Text = string.Empty, Height = 38, FontSize = 14, Padding = new Thickness(10), Margin = new Thickness(0, 3, 8, 0) };
        textStack.Children.Add(_reminderTextInput);
        form.Children.Add(textStack);

        var timeStack = new StackPanel();
        timeStack.Children.Add(MakeText("Время (ЧЧ:ММ)", 11, Brush("#AAB6CA")));
        _reminderTimeInput = new TextBox { Text = "20:00", Height = 38, FontSize = 14, Padding = new Thickness(9), Margin = new Thickness(0, 3, 8, 0), MaxLength = 5 };
        timeStack.Children.Add(_reminderTimeInput);
        Grid.SetColumn(timeStack, 1);
        form.Children.Add(timeStack);

        var add = MakeButton("Добавить", (_, _) => AddReminder(), "#22766F");
        add.Height = 38; add.Width = 92; add.Margin = new Thickness(0, 20, 0, 0);
        Grid.SetColumn(add, 2);
        form.Children.Add(add);

        var repeatPanel = new StackPanel { Margin = new Thickness(0, 11, 0, 0) };
        repeatPanel.Children.Add(MakeText("Повторение", 10, Brush("#AAB6CA")));
        _reminderRepeatModeCombo = MakeCombo(new[] { "Каждый день", "По дням недели", "Без повтора" });
        _reminderRepeatModeCombo.SelectedIndex = 0;
        _reminderRepeatModeCombo.Margin = new Thickness(0, 3, 0, 5);
        repeatPanel.Children.Add(_reminderRepeatModeCombo);

        var oneTimeHint = MakeText(
            "Если выбранное время сегодня уже прошло (или совпадает с текущим), напоминание сработает завтра в это время.",
            10, Brush("#91A1B9"));
        oneTimeHint.TextWrapping = TextWrapping.Wrap;
        oneTimeHint.Margin = new Thickness(1, 2, 0, 5);
        oneTimeHint.Visibility = Visibility.Collapsed;
        repeatPanel.Children.Add(oneTimeHint);

        var daysGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        string[] days = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        _reminderDayCheckBoxes.Clear();
        for (int i = 0; i < days.Length; i++)
        {
            var check = new CheckBox
            {
                Content = LocalizationService.Translate(days[i]), IsChecked = false, Foreground = Brush("#EAF0F8"),
                FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center,
                ToolTip = LocalizationService.Translate(new[] { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота", "Воскресенье" }[i])
            };
            _reminderDayCheckBoxes.Add(check);
            daysGrid.Children.Add(check);
        }
        repeatPanel.Children.Add(daysGrid);
        _reminderRepeatModeCombo.SelectionChanged += (_, _) =>
        {
            string mode = _reminderRepeatModeCombo.SelectedItem as string ?? "Каждый день";
            oneTimeHint.Visibility = mode == "Без повтора" ? Visibility.Visible : Visibility.Collapsed;
            daysGrid.Visibility = mode == "По дням недели" ? Visibility.Visible : Visibility.Collapsed;
        };
        Grid.SetRow(repeatPanel, 1);
        Grid.SetColumnSpan(repeatPanel, 3);
        form.Children.Add(repeatPanel);

        addCard.Child = form;
        DockPanel.SetDock(addCard, Dock.Top);
        root.Children.Add(addCard);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var list = new StackPanel();
        if (_appData.Reminders.Count == 0)
        {
            list.Children.Add(MakeText("Пока нет напоминаний", 16, Brush("#AAB6CA")));
            list.Children.Add(MakeText("Добавь напоминание и оставь Orbiq работать в системном трее.", 12, Brush("#91A1B9")));
        }
        foreach (ReminderItem reminder in _appData.Reminders.OrderBy(r => r.Time, StringComparer.Ordinal))
        {
            var card = new Border { Background = Brush("#1B2A40"), BorderBrush = Brush("#354862"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 9) };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var content = new StackPanel();
            var check = new CheckBox
            {
                Content = reminder.Text,
                IsChecked = reminder.IsEnabled,
                IsEnabled = !(reminder.IsOneTime && !string.IsNullOrWhiteSpace(reminder.LastTriggeredDate)),
                Foreground = Brush("#FFFFFF"),
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            check.Checked += (_, _) => { reminder.IsEnabled = true; SaveData(); };
            check.Unchecked += (_, _) => { reminder.IsEnabled = false; SaveData(); };
            content.Children.Add(check);
            string[] shortDays = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }.Select(LocalizationService.Translate).ToArray();
            DateTime scheduledDate = reminder.OneTimeDate?.Date ?? DateTime.Today;
            string scheduledDayLabel = scheduledDate == DateTime.Today
                ? LocalizationService.Translate("Сегодня")
                : scheduledDate == DateTime.Today.AddDays(1)
                    ? LocalizationService.Translate("Завтра")
                    : scheduledDate.ToString("d", LocalizationService.CurrentCulture);
            string repeatText = reminder.IsOneTime
                ? LocalizationService.Translate($"Без повтора · {scheduledDayLabel}")
                : reminder.RepeatDays.Count == 0 || reminder.RepeatDays.Count == 7
                    ? LocalizationService.Translate("Каждый день")
                    : string.Join(" · ", reminder.RepeatDays.Where(d => d is >= 1 and <= 7).Distinct().OrderBy(d => d).Select(d => shortDays[d - 1]));
            content.Children.Add(MakeText($"Повтор: {repeatText}", 10, Brush("#91A1B9")));
            if (reminder.SnoozeUntil.HasValue)
                content.Children.Add(MakeText($"Отложено до {reminder.SnoozeUntil.Value:HH:mm}", 9, Brush("#64D8CB")));
            if (reminder.IsOneTime && !string.IsNullOrWhiteSpace(reminder.LastTriggeredDate))
                content.Children.Add(MakeText("Напоминание уже сработало", 9, Brush("#91A1B9")));
            row.Children.Add(content);

            var timeText = MakeText(reminder.Time, 18, Brush("#64D8CB"), FontWeights.SemiBold);
            timeText.VerticalAlignment = VerticalAlignment.Center;
            timeText.Margin = new Thickness(12, 0, 12, 0);
            Grid.SetColumn(timeText, 1);
            row.Children.Add(timeText);
            var delete = MakeButton("×", (_, _) => ShowConfirm("Удалить напоминание?",
                LocalizationService.Translate("Удалить «{text}»?").Replace("{text}", reminder.Text, StringComparison.Ordinal), () =>
            {
                _appData.Reminders.RemoveAll(r => r.Id == reminder.Id);
                SaveData();
                ContentArea.Content = BuildRemindersPage();
            }));
            delete.Width = 38; delete.Height = 34; delete.Margin = new Thickness(0);
            Grid.SetColumn(delete, 2);
            row.Children.Add(delete);
            var context = new ContextMenu();
            foreach (int minutes in new[] { 5, 10, 30 })
            {
                int delay = minutes;
                var snooze = new MenuItem { Header = LocalizationService.Translate($"Отложить на {delay} минут") };
                snooze.Click += (_, _) =>
                {
                    reminder.IsEnabled = true;
                    reminder.SnoozeUntil = DateTime.Now.AddMinutes(delay);
                    SaveData();
                    ContentArea.Content = BuildRemindersPage();
                };
                context.Items.Add(snooze);
            }
            card.ContextMenu = context;
            card.Child = row;
            list.Children.Add(card);
        }
        scroll.Content = list;
        root.Children.Add(scroll);
        return root;
    }

    private static DateTime GetNextOneTimeDate(string timeText, DateTime now)
    {
        DateTime clockTime = DateTime.ParseExact(
            timeText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None);
        DateTime candidate = now.Date.Add(clockTime.TimeOfDay);

        // Если выбранное время уже наступило сегодня, включая текущую минуту,
        // одноразовое напоминание переносится на завтра.
        if (candidate <= now)
            candidate = candidate.AddDays(1);

        return candidate.Date;
    }

    private void AddReminder()
    {
        string message = _reminderTextInput?.Text.Trim() ?? string.Empty;
        string timeText = _reminderTimeInput?.Text.Trim() ?? string.Empty;
        if (message.Length == 0)
        {
            ShowNotice("Напоминание", "Напиши текст напоминания.");
            return;
        }
        if (!Regex.IsMatch(timeText, "^(?:[01][0-9]|2[0-3]):[0-5][0-9]$"))
        {
            ShowNotice("Неправильное время", "Введи время в формате ЧЧ:ММ, например 20:00.");
            return;
        }

        string mode = _reminderRepeatModeCombo?.SelectedItem as string ?? "Каждый день";
        bool oneTime = mode is "Без повтора" or "Один раз";
        DateTime? oneTimeDate = oneTime ? GetNextOneTimeDate(timeText, DateTime.Now) : null;
        List<int> repeatDays = new();
        if (!oneTime && mode == "По дням недели")
        {
            repeatDays = _reminderDayCheckBoxes
                .Select((check, index) => new { check, day = index + 1 })
                .Where(item => item.check.IsChecked == true)
                .Select(item => item.day).ToList();
            if (repeatDays.Count == 0)
            {
                ShowNotice("Дни повтора", "Выбери хотя бы один день недели.");
                return;
            }
        }

        _appData.Reminders.Add(new ReminderItem
        {
            Text = message,
            Time = timeText,
            IsEnabled = true,
            IsOneTime = oneTime,
            OneTimeDate = oneTimeDate,
            RepeatDays = repeatDays
        });
        SaveData();
        ContentArea.Content = BuildRemindersPage();
    }

    private void ReminderTicker_Tick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string minuteKey = now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        int today = ((int)now.DayOfWeek + 6) % 7 + 1;
        List<string> due = new();
        List<ReminderItem> dueReminders = new();
        foreach (ReminderItem reminder in _appData.Reminders)
        {
            if (!reminder.IsEnabled) continue;
            if (reminder.SnoozeUntil is DateTime snoozeUntil && snoozeUntil <= now)
            {
                reminder.SnoozeUntil = null;
                reminder.LastTriggeredDate = date;
                if (reminder.IsOneTime) reminder.IsEnabled = false;
                due.Add(reminder.Text);
                dueReminders.Add(reminder);
                continue;
            }
            bool repeatToday = reminder.IsOneTime
                ? reminder.OneTimeDate?.Date == now.Date
                : reminder.RepeatDays.Count == 0 || reminder.RepeatDays.Contains(today);
            bool validReminderTime = DateTime.TryParseExact(reminder.Time, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime reminderClockTime);
            // Если приложение было свёрнуто/компьютер выходил из сна в течение дня,
            // показываем пропущенное напоминание при первом следующем опросе.
            if (!repeatToday || !validReminderTime ||
                now.TimeOfDay < reminderClockTime.TimeOfDay || reminder.LastTriggeredDate == date) continue;
            reminder.LastTriggeredDate = date;
            if (reminder.IsOneTime) reminder.IsEnabled = false;
            due.Add(reminder.Text);
            dueReminders.Add(reminder);
        }

        foreach (CalendarEventItem calendarEvent in _appData.CalendarEvents)
        {
            bool validEventTime = DateTime.TryParseExact(calendarEvent.Time, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime eventClockTime);
            if (calendarEvent.IsCompleted || calendarEvent.Date.Date != now.Date || !validEventTime ||
                now.TimeOfDay < eventClockTime.TimeOfDay ||
                string.Equals(calendarEvent.LastTriggeredMinute, minuteKey, StringComparison.Ordinal)) continue;
            calendarEvent.LastTriggeredMinute = minuteKey;
            calendarEvent.IsCompleted = true;
            due.Add($"Календарь: {calendarEvent.Title}");
        }

        if (due.Count == 0) return;
        SaveData();
        ShowReminderToast(due.Count == 1 ? "Напоминание" : "События и напоминания",
            string.Join(Environment.NewLine + Environment.NewLine, due), dueReminders);
    }

    private void ShowReminderToast(string title, string message, IReadOnlyList<ReminderItem>? snoozableReminders = null)
    {
        PlayNotificationSound();
        if (_toastWindow is not null)
        {
            try { _toastWindow.Close(); } catch { }
        }
        Action<int>? snoozeAction = snoozableReminders is null || snoozableReminders.Count == 0
            ? null
            : minutes =>
            {
                DateTime until = DateTime.Now.AddMinutes(minutes);
                foreach (ReminderItem reminder in snoozableReminders)
                {
                    reminder.IsEnabled = true;
                    reminder.SnoozeUntil = until;
                }
                SaveData();
            };
        _toastWindow = new ReminderToastWindow(title, message, snoozeAction);
        _toastWindow.Closed += (_, _) => _toastWindow = null;
        _toastWindow.Show();
    }

    // ----------------------------- POMODORO -----------------------------

    private UIElement BuildPomodoroPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(MakeText("Pomodoro", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText("Чередуй сосредоточенную работу и короткие перерывы", 12, Brush("#AAB6CA")));
        root.Children.Add(heading);

        var body = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.85, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var timerCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 0)
        };
        var timerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _pomodoroPhaseText = MakeText(LocalizationService.Translate(_pomodoroPhase).ToUpper(LocalizationService.CurrentCulture), 12, Brush("#64D8CB"), FontWeights.SemiBold, translate: false);
        _pomodoroPhaseText.TextAlignment = TextAlignment.Center;
        _pomodoroPhaseText.Margin = new Thickness(0, 0, 0, 8);
        timerStack.Children.Add(_pomodoroPhaseText);

        var ring = new Grid { Width = 224, Height = 224, HorizontalAlignment = HorizontalAlignment.Center };
        ring.Children.Add(new Ellipse { Stroke = Brush("#33445B"), StrokeThickness = 9 });
        _pomodoroArc = new ShapePath
        {
            Fill = Brushes.Transparent, Stroke = Brush("#64D8CB"), StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        };
        ring.Children.Add(_pomodoroArc);
        _pomodoroTimeText = MakeText(FormatPomodoroTime(_pomodoroRemaining), 39, Brush("#FFFFFF"), FontWeights.Light);
        _pomodoroTimeText.TextAlignment = TextAlignment.Center;
        _pomodoroTimeText.HorizontalAlignment = HorizontalAlignment.Center;
        _pomodoroTimeText.VerticalAlignment = VerticalAlignment.Center;
        ring.Children.Add(_pomodoroTimeText);
        timerStack.Children.Add(ring);

        _pomodoroSessionsText = MakeText($"Завершено рабочих сессий: {_appData.Settings.PomodoroCompletedSessions}",
            10, Brush("#91A1B9"));
        _pomodoroSessionsText.TextAlignment = TextAlignment.Center;
        _pomodoroSessionsText.Margin = new Thickness(0, 10, 0, 7);
        timerStack.Children.Add(_pomodoroSessionsText);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        _pomodoroStartButton = MakeButton(_pomodoroRunning ? "Пауза" : "Старт", (_, _) => TogglePomodoro(), "#22766F");
        _pomodoroStartButton.Width = 105;
        var reset = MakeButton("Сброс", (_, _) => ResetPomodoro());
        reset.Width = 90;
        var skip = MakeButton("Пропустить", (_, _) => SkipPomodoroPhase());
        skip.Width = 105;
        buttons.Children.Add(_pomodoroStartButton);
        buttons.Children.Add(reset);
        buttons.Children.Add(skip);
        timerStack.Children.Add(buttons);
        timerCard.Child = timerStack;
        body.Children.Add(timerCard);

        var settingsCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(13), Margin = new Thickness(0)
        };
        Grid.SetColumn(settingsCard, 1);
        var settings = new StackPanel();
        settings.Children.Add(MakeText("ИНТЕРВАЛЫ", 11, Brush("#64D8CB"), FontWeights.SemiBold));
        settings.Children.Add(MakeText("Работа (минут)", 11, Brush("#AAB6CA")));
        _pomodoroWorkInput = MakePomodoroNumberBox(_appData.Settings.PomodoroWorkMinutes);
        settings.Children.Add(_pomodoroWorkInput);
        settings.Children.Add(MakeText("Короткий перерыв (минут)", 11, Brush("#AAB6CA")));
        _pomodoroShortBreakInput = MakePomodoroNumberBox(_appData.Settings.PomodoroShortBreakMinutes);
        settings.Children.Add(_pomodoroShortBreakInput);
        settings.Children.Add(MakeText("Длинный перерыв (минут)", 11, Brush("#AAB6CA")));
        _pomodoroLongBreakInput = MakePomodoroNumberBox(_appData.Settings.PomodoroLongBreakMinutes);
        settings.Children.Add(_pomodoroLongBreakInput);
        settings.Children.Add(MakeText("Рабочих сессий до длинного перерыва", 11, Brush("#AAB6CA")));
        _pomodoroCyclesInput = MakePomodoroNumberBox(_appData.Settings.PomodoroCyclesBeforeLongBreak);
        settings.Children.Add(_pomodoroCyclesInput);

        var saveIntervals = MakeButton("Сохранить интервалы", (_, _) => SavePomodoroIntervals(), "#22766F");
        saveIntervals.HorizontalAlignment = HorizontalAlignment.Stretch;
        saveIntervals.Margin = new Thickness(0, 12, 0, 0);
        settings.Children.Add(saveIntervals);
        settings.Children.Add(new Border { Height = 1, Background = Brush("#35445A"), Margin = new Thickness(0, 15, 0, 12) });
        settings.Children.Add(MakeText("Как это работает", 13, Brush("#FFFFFF"), FontWeights.SemiBold));
        settings.Children.Add(MakeText("После завершения рабочего интервала Orbiq подготовит перерыв. После заданного числа рабочих сессий будет предложен длинный отдых. Следующий этап запускается кнопкой «Старт».", 11, Brush("#91A1B9")));
        var settingsScroll = new ScrollViewer
        {
            Content = settings,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        settingsCard.Child = settingsScroll;
        body.Children.Add(settingsCard);

        UpdatePomodoroDisplay();
        return root;
    }

    private TextBox MakePomodoroNumberBox(int value)
    {
        var box = new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture), Height = 34, FontSize = 13,
            Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 3, 0, 9),
            VerticalContentAlignment = VerticalAlignment.Center, MaxLength = 3
        };
        box.PreviewTextInput += (_, args) => args.Handled = !Regex.IsMatch(args.Text, "^[0-9]+$");
        return box;
    }

    private void SavePomodoroIntervals()
    {
        if (!int.TryParse(_pomodoroWorkInput?.Text, out int work) || work is < 1 or > 180 ||
            !int.TryParse(_pomodoroShortBreakInput?.Text, out int shortBreak) || shortBreak is < 1 or > 90 ||
            !int.TryParse(_pomodoroLongBreakInput?.Text, out int longBreak) || longBreak is < 1 or > 180 ||
            !int.TryParse(_pomodoroCyclesInput?.Text, out int cycles) || cycles is < 1 or > 12)
        {
            ShowNotice("Некорректные интервалы", "Работа: 1–180 минут, короткий перерыв: 1–90, длинный перерыв: 1–180, число рабочих сессий: 1–12.");
            return;
        }

        _appData.Settings.PomodoroWorkMinutes = work;
        _appData.Settings.PomodoroShortBreakMinutes = shortBreak;
        _appData.Settings.PomodoroLongBreakMinutes = longBreak;
        _appData.Settings.PomodoroCyclesBeforeLongBreak = cycles;
        if (!_pomodoroRunning)
        {
            _pomodoroPhaseDuration = PomodoroDurationForPhase();
            _pomodoroRemaining = _pomodoroPhaseDuration;
        }
        SaveData();
        UpdatePomodoroDisplay();
        ShowNotice("Интервалы сохранены", "Новые значения будут использоваться в текущем и следующих циклах Pomodoro.");
    }

    private TimeSpan PomodoroDurationForPhase() => _pomodoroPhase switch
    {
        "Короткий перерыв" => TimeSpan.FromMinutes(_appData.Settings.PomodoroShortBreakMinutes),
        "Длинный перерыв" => TimeSpan.FromMinutes(_appData.Settings.PomodoroLongBreakMinutes),
        _ => TimeSpan.FromMinutes(_appData.Settings.PomodoroWorkMinutes)
    };

    private static string FormatPomodoroTime(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        int totalMinutes = (int)Math.Floor(value.TotalMinutes);
        return $"{totalMinutes:00}:{value.Seconds:00}";
    }

    private void TogglePomodoro()
    {
        if (_pomodoroRunning)
        {
            _pomodoroRemaining = _pomodoroEndsAt - DateTime.Now;
            if (_pomodoroRemaining < TimeSpan.Zero) _pomodoroRemaining = TimeSpan.Zero;
            _pomodoroRunning = false;
            _pomodoroTicker.Stop();
        }
        else
        {
            if (_pomodoroRemaining <= TimeSpan.Zero)
            {
                _pomodoroPhaseDuration = PomodoroDurationForPhase();
                _pomodoroRemaining = _pomodoroPhaseDuration;
            }
            _pomodoroEndsAt = DateTime.Now.Add(_pomodoroRemaining);
            _pomodoroRunning = true;
            _pomodoroTicker.Start();
        }
        UpdatePomodoroDisplay();
    }

    private void ResetPomodoro()
    {
        _pomodoroTicker.Stop();
        _pomodoroRunning = false;
        _pomodoroPhase = "Работа";
        _pomodoroPhaseDuration = PomodoroDurationForPhase();
        _pomodoroRemaining = _pomodoroPhaseDuration;
        UpdatePomodoroDisplay();
    }

    private void SkipPomodoroPhase()
    {
        _pomodoroTicker.Stop();
        _pomodoroRunning = false;
        AdvancePomodoroPhase(completedNormally: false);
    }

    private void PomodoroTicker_Tick(object? sender, EventArgs e)
    {
        if (!_pomodoroRunning) return;
        _pomodoroRemaining = _pomodoroEndsAt - DateTime.Now;
        if (_pomodoroRemaining <= TimeSpan.Zero)
        {
            _pomodoroRemaining = TimeSpan.Zero;
            _pomodoroRunning = false;
            _pomodoroTicker.Stop();
            AdvancePomodoroPhase(completedNormally: true);
            return;
        }
        UpdatePomodoroDisplay();
    }

    private void AdvancePomodoroPhase(bool completedNormally)
    {
        string finishedPhase = _pomodoroPhase;
        if (finishedPhase == "Работа")
        {
            if (completedNormally)
            {
                _appData.Settings.PomodoroCompletedSessions++;
                int sessionsPerCycle = Math.Max(1, _appData.Settings.PomodoroCyclesBeforeLongBreak);
                _pomodoroPhase = _appData.Settings.PomodoroCompletedSessions % sessionsPerCycle == 0
                    ? "Длинный перерыв" : "Короткий перерыв";
            }
            else
            {
                _pomodoroPhase = "Короткий перерыв";
            }
        }
        else
        {
            _pomodoroPhase = "Работа";
        }

        _pomodoroPhaseDuration = PomodoroDurationForPhase();
        _pomodoroRemaining = _pomodoroPhaseDuration;
        SaveData();
        UpdatePomodoroDisplay();
        string finishedPhaseText = LocalizationService.Translate(finishedPhase);
        string nextPhaseText = LocalizationService.Translate(_pomodoroPhase).ToLower(LocalizationService.CurrentCulture);
        string phaseMessage = completedNormally
            ? LocalizationService.Translate("Этап «{phase}» завершён. Следующий этап: {next}.")
                .Replace("{phase}", finishedPhaseText, StringComparison.Ordinal)
                .Replace("{next}", nextPhaseText, StringComparison.Ordinal)
            : LocalizationService.Translate("Этап пропущен. Следующий этап: {next}.")
                .Replace("{next}", nextPhaseText, StringComparison.Ordinal);
        ShowReminderToast("Pomodoro", phaseMessage);
    }

    private void UpdatePomodoroDisplay()
    {
        if (_pomodoroTimeText is null) return;
        _pomodoroTimeText.Text = FormatPomodoroTime(_pomodoroRemaining);
        if (_pomodoroPhaseText is not null) _pomodoroPhaseText.Text = LocalizationService.Translate(_pomodoroPhase).ToUpper(LocalizationService.CurrentCulture);
        if (_pomodoroSessionsText is not null)
            _pomodoroSessionsText.Text = LocalizationService.Translate($"Завершено рабочих сессий: {_appData.Settings.PomodoroCompletedSessions}");
        if (_pomodoroStartButton is not null) _pomodoroStartButton.Content = LocalizationService.Translate(_pomodoroRunning ? "Пауза" : "Старт");
        double duration = Math.Max(1, _pomodoroPhaseDuration.TotalSeconds);
        double progress = 1.0 - Math.Clamp(_pomodoroRemaining.TotalSeconds / duration, 0, 1);
        UpdateCircularProgress(_pomodoroArc, 112, progress);
    }

    private static void UpdateCircularProgress(ShapePath? arc, double radius, double progress)
    {
        if (arc is null) return;
        const double center = 112;
        progress = Math.Clamp(progress, 0, 1);
        if (progress <= 0.001)
        {
            arc.Data = Geometry.Empty;
            return;
        }
        if (progress >= 0.999)
        {
            arc.Data = new EllipseGeometry(new Point(center, center), radius, radius);
            return;
        }
        double angle = -90 + 360 * progress;
        double radians = angle * Math.PI / 180;
        var end = new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(center, center - radius), false, false);
            context.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        arc.Data = geometry;
    }

    // ----------------------------- КАЛЕНДАРЬ -----------------------------

    private UIElement BuildCalendarPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(MakeText("Календарь", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText("События на выбранные даты и уведомления по времени", 12, Brush("#AAB6CA")));
        root.Children.Add(heading);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var calendarCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 9, 0)
        };
        var calendarStack = new StackPanel();
        var monthHeader = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var previous = MakeButton("‹", (_, _) => MoveCalendarMonth(-1));
        previous.Width = 34; previous.Height = 32; previous.Margin = new Thickness(0, 0, 3, 0); previous.Padding = new Thickness(0);
        monthHeader.Children.Add(previous);
        var monthTitle = MakeText(_calendarMonth.ToString("MMMM yyyy", LocalizationService.CurrentCulture),
            14, Brush("#FFFFFF"), FontWeights.SemiBold);
        monthTitle.TextAlignment = TextAlignment.Center;
        monthTitle.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(monthTitle, 1);
        monthHeader.Children.Add(monthTitle);
        var next = MakeButton("›", (_, _) => MoveCalendarMonth(1));
        next.Width = 34; next.Height = 32; next.Margin = new Thickness(3, 0, 0, 0); next.Padding = new Thickness(0);
        Grid.SetColumn(next, 2);
        monthHeader.Children.Add(next);
        calendarStack.Children.Add(monthHeader);

        var weekdayGrid = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
        string[] weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        foreach (string weekday in weekdays)
        {
            var label = MakeText(weekday, 9, Brush("#91A1B9"), FontWeights.SemiBold);
            label.TextAlignment = TextAlignment.Center;
            label.Margin = new Thickness(0, 2, 0, 3);
            weekdayGrid.Children.Add(label);
        }
        calendarStack.Children.Add(weekdayGrid);

        var daysGrid = new UniformGrid { Columns = 7, Rows = 6 };
        DateTime firstDay = new(_calendarMonth.Year, _calendarMonth.Month, 1);
        int offset = ((int)firstDay.DayOfWeek + 6) % 7;
        DateTime gridStart = firstDay.AddDays(-offset);
        for (int i = 0; i < 42; i++)
        {
            DateTime date = gridStart.AddDays(i);
            bool currentMonth = date.Month == _calendarMonth.Month;
            bool selected = date.Date == _calendarSelectedDate.Date;
            int eventCount = _appData.CalendarEvents.Count(item => item.Date.Date == date.Date && !item.IsCompleted);
            string cellText = eventCount > 0 ? $"{date.Day}\n•" : date.Day.ToString(CultureInfo.InvariantCulture);
            var dayButton = MakeButton(cellText, (_, _) =>
            {
                _calendarSelectedDate = date.Date;
                ContentArea.Content = BuildCalendarPage();
            }, selected ? "#22766F" : eventCount > 0 ? "#30435C" : "#202F45");
            dayButton.Height = 39;
            dayButton.MinWidth = 0;
            dayButton.Margin = new Thickness(2);
            dayButton.Padding = new Thickness(1);
            dayButton.FontSize = eventCount > 0 ? 10 : 12;
            dayButton.Opacity = currentMonth ? 1 : 0.34;
            dayButton.ToolTip = eventCount > 0 ? LocalizationService.Translate($"Событий: {eventCount}") : date.ToString("d MMMM yyyy", LocalizationService.CurrentCulture);
            daysGrid.Children.Add(dayButton);
        }
        calendarStack.Children.Add(daysGrid);
        calendarStack.Children.Add(MakeText("• — на эту дату есть незавершённые события", 9, Brush("#91A1B9")));
        calendarCard.Child = calendarStack;
        body.Children.Add(calendarCard);

        var eventsCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12)
        };
        Grid.SetColumn(eventsCard, 1);
        var eventsStack = new DockPanel();
        var eventHeader = new StackPanel();
        eventHeader.Children.Add(MakeText(_calendarSelectedDate.ToString("d MMMM", LocalizationService.CurrentCulture),
            16, Brush("#FFFFFF"), FontWeights.SemiBold));
        eventHeader.Children.Add(MakeText(_calendarSelectedDate.ToString("dddd", LocalizationService.CurrentCulture),
            10, Brush("#91A1B9")));
        var addEvent = MakeButton("＋  Добавить событие", (_, _) => ShowCalendarEventEditor(_calendarSelectedDate), "#22766F");
        addEvent.HorizontalAlignment = HorizontalAlignment.Stretch;
        addEvent.Margin = new Thickness(0, 8, 0, 10);
        eventHeader.Children.Add(addEvent);
        DockPanel.SetDock(eventHeader, Dock.Top);
        eventsStack.Children.Add(eventHeader);

        var eventScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var eventList = new StackPanel();
        List<CalendarEventItem> selectedEvents = _appData.CalendarEvents
            .Where(item => item.Date.Date == _calendarSelectedDate.Date)
            .OrderBy(item => item.Time, StringComparer.Ordinal).ToList();
        if (selectedEvents.Count == 0)
        {
            eventList.Children.Add(MakeText("На этот день событий нет", 11, Brush("#91A1B9")));
        }
        foreach (CalendarEventItem calendarEvent in selectedEvents)
        {
            var eventCard = new Border
            {
                Background = Brush("#202F45"), CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9), Margin = new Thickness(0, 0, 0, 7)
            };
            var eventGrid = new Grid();
            eventGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            eventGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            var eventText = new StackPanel();
            eventText.Children.Add(MakeText(calendarEvent.Time, 12, Brush("#64D8CB"), FontWeights.SemiBold));
            var titleText = MakeText(calendarEvent.Title, 11, Brush("#FFFFFF"), FontWeights.Medium, translate: false);
            if (calendarEvent.IsCompleted) titleText.TextDecorations = TextDecorations.Strikethrough;
            eventText.Children.Add(titleText);
            if (!string.IsNullOrWhiteSpace(calendarEvent.Details))
                eventText.Children.Add(MakeText(calendarEvent.Details, 9, Brush("#91A1B9"), translate: false));
            if (calendarEvent.IsCompleted)
                eventText.Children.Add(MakeText("Уведомление показано", 9, Brush("#91A1B9")));
            eventGrid.Children.Add(eventText);
            var deleteEvent = MakeButton("×", (_, _) => ShowConfirm("Удалить событие?",
                LocalizationService.Translate("Удалить «{title}» из календаря?").Replace("{title}", calendarEvent.Title, StringComparison.Ordinal), () =>
                {
                    _appData.CalendarEvents.RemoveAll(item => item.Id == calendarEvent.Id);
                    SaveData();
                    ContentArea.Content = BuildCalendarPage();
                }));
            deleteEvent.Width = 30; deleteEvent.Height = 30; deleteEvent.Margin = new Thickness(0);
            deleteEvent.Padding = new Thickness(0);
            deleteEvent.HorizontalAlignment = HorizontalAlignment.Center;
            deleteEvent.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(deleteEvent, 1);
            eventGrid.Children.Add(deleteEvent);
            eventCard.Child = eventGrid;
            eventList.Children.Add(eventCard);
        }
        eventScroll.Content = eventList;
        eventsStack.Children.Add(eventScroll);
        eventsCard.Child = eventsStack;
        body.Children.Add(eventsCard);

        return root;
    }

    private void MoveCalendarMonth(int offset)
    {
        _calendarMonth = _calendarMonth.AddMonths(offset);
        if (_calendarSelectedDate.Year != _calendarMonth.Year || _calendarSelectedDate.Month != _calendarMonth.Month)
            _calendarSelectedDate = new DateTime(_calendarMonth.Year, _calendarMonth.Month, 1);
        ContentArea.Content = BuildCalendarPage();
    }

    private void ShowCalendarEventEditor(DateTime date)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(MakeText("Новое событие", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText(date.ToString("d MMMM yyyy", LocalizationService.CurrentCulture), 12, Brush("#AAB6CA")));
        root.Children.Add(heading);

        var card = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRow(card, 1);
        var form = new StackPanel();
        form.Children.Add(MakeText("Название события", 11, Brush("#AAB6CA")));
        var titleInput = new TextBox { Height = 38, Padding = new Thickness(9, 7, 9, 7), MaxLength = 100 };
        form.Children.Add(titleInput);
        form.Children.Add(MakeText("Время (ЧЧ:ММ)", 11, Brush("#AAB6CA")));
        var timeInput = new TextBox { Text = "09:00", Height = 38, Padding = new Thickness(9, 7, 9, 7), MaxLength = 5, Margin = new Thickness(0, 0, 0, 9) };
        form.Children.Add(timeInput);
        form.Children.Add(MakeText("Описание (необязательно)", 11, Brush("#AAB6CA")));
        var detailsInput = new TextBox { Height = 86, Padding = new Thickness(9), AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 500 };
        form.Children.Add(detailsInput);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(MakeButton("Сохранить событие", (_, _) =>
        {
            string title = titleInput.Text.Trim();
            string time = timeInput.Text.Trim();
            if (title.Length == 0)
            {
                ShowNotice("Календарь", "Введи название события.");
                return;
            }
            if (!Regex.IsMatch(time, "^(?:[01][0-9]|2[0-3]):[0-5][0-9]$"))
            {
                ShowNotice("Неправильное время", "Введи время в формате ЧЧ:ММ, например 09:30.");
                return;
            }
            _appData.CalendarEvents.Add(new CalendarEventItem
            {
                Title = title,
                Date = date.Date,
                Time = time,
                Details = detailsInput.Text.Trim()
            });
            _calendarMonth = new DateTime(date.Year, date.Month, 1);
            _calendarSelectedDate = date.Date;
            SaveData();
            NavigateTo("Calendar");
        }, "#22766F"));
        buttons.Children.Add(MakeButton("Отмена", (_, _) => NavigateTo("Calendar")));
        form.Children.Add(buttons);
        card.Child = form;
        root.Children.Add(card);
        ContentArea.Content = root;
    }

    // ----------------------------- МОНИТОРИНГ СИСТЕМЫ -----------------------------

    private UIElement BuildMonitorPage()
    {
        _monitorMetrics.Clear();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var heading = new Grid { Margin = new Thickness(2, 0, 0, 14) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(MakeText("Мониторинг компьютера", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        titleStack.Children.Add(MakeText("Основные показатели системы обновляются автоматически", 12, Brush("#AAB6CA")));
        heading.Children.Add(titleStack);
        var refresh = MakeButton("↻", (_, _) => UpdateMonitorCards());
        refresh.Width = 36; refresh.Height = 34; refresh.Padding = new Thickness(0);
        Grid.SetColumn(refresh, 1);
        heading.Children.Add(refresh);
        root.Children.Add(heading);

        var cardsGrid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        cardsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cardsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        cardsGrid.Children.Add(CreateMonitorMetricCard("cpu", "Процессор", "Общая загрузка процессора", 0, 0));
        cardsGrid.Children.Add(CreateMonitorMetricCard("memory", "Оперативная память", "Использование физической памяти", 0, 1));
        cardsGrid.Children.Add(CreateMonitorMetricCard("battery", "Питание", "Состояние аккумулятора", 1, 0));
        cardsGrid.Children.Add(CreateMonitorMetricCard("disk", "Системный диск", "Свободное место на диске Windows", 1, 1));
        Grid.SetRow(cardsGrid, 1);
        root.Children.Add(cardsGrid);
        UpdateMonitorCards();
        _monitorTicker.Start();
        return root;
    }

    private Border CreateMonitorMetricCard(string key, string title, string description, int row, int column)
    {
        var card = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14),
            Margin = new Thickness(column == 0 ? 0 : 6, row == 0 ? 0 : 6, column == 1 ? 0 : 6, row == 1 ? 0 : 6)
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(MakeText(title, 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var value = MakeText("—", 26, Brush("#64D8CB"), FontWeights.Light);
        value.Margin = new Thickness(0, 8, 0, 3);
        stack.Children.Add(value);
        var details = MakeText(description, 10, Brush("#91A1B9"));
        stack.Children.Add(details);
        var track = new Border
        {
            Background = Brush("#26364D"), CornerRadius = new CornerRadius(5), Height = 8,
            Margin = new Thickness(0, 12, 0, 0), ClipToBounds = true
        };
        var fill = new Border { Background = Brush("#64D8CB"), CornerRadius = new CornerRadius(5), Width = 0, HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = fill;
        track.SizeChanged += (_, _) => UpdateMetricFill(track, fill, _monitorMetrics.TryGetValue(key, out MonitorMetricUi? metric) ? metric.Percent : 0);
        stack.Children.Add(track);
        card.Child = stack;
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        _monitorMetrics[key] = new MonitorMetricUi(value, details, track, fill);
        return card;
    }

    private static void UpdateMetricFill(Border track, Border fill, double percent)
    {
        fill.Width = Math.Max(0, track.ActualWidth * Math.Clamp(percent, 0, 100) / 100d);
        fill.Height = track.ActualHeight;
    }

    private void SetMonitorMetric(string key, string value, string details, double percent)
    {
        if (!_monitorMetrics.TryGetValue(key, out MonitorMetricUi? metric)) return;
        metric.Value.Text = value;
        metric.Details.Text = LocalizationService.Translate(details);
        metric.Percent = Math.Clamp(percent, 0, 100);
        UpdateMetricFill(metric.Track, metric.Fill, metric.Percent);
    }

    private void MonitorTicker_Tick(object? sender, EventArgs e)
    {
        if (_currentPage == "Monitor") UpdateMonitorCards();
    }

    private void UpdateMonitorCards()
    {
        if (_monitorMetrics.Count == 0) return;
        try
        {
            SystemMetrics metrics = _systemMonitor.Read();
            SetMonitorMetric("cpu", $"{metrics.CpuPercent:0}%", "Общая загрузка процессора", metrics.CpuPercent);
            SetMonitorMetric("memory", $"{metrics.UsedMemoryGb:0.0} / {metrics.TotalMemoryGb:0.0} ГБ",
                $"Занято {metrics.MemoryPercent:0}% физической памяти", metrics.MemoryPercent);

            if (metrics.BatteryPercent.HasValue)
            {
                string powerState = metrics.IsPluggedIn ? "Подключено питание" : "Работа от аккумулятора";
                SetMonitorMetric("battery", $"{metrics.BatteryPercent.Value}%", powerState, metrics.BatteryPercent.Value);
            }
            else
            {
                SetMonitorMetric("battery", "Нет данных", metrics.IsPluggedIn ? "Питание от сети" : "Сведения об аккумуляторе недоступны", 0);
            }

            double diskUsed = metrics.SystemDriveTotalGb <= 0 ? 0 :
                100d * (metrics.SystemDriveTotalGb - metrics.SystemDriveFreeGb) / metrics.SystemDriveTotalGb;
            SetMonitorMetric("disk", $"{metrics.SystemDriveFreeGb:0.0} ГБ свободно",
                $"Из {metrics.SystemDriveTotalGb:0.0} ГБ на системном диске", diskUsed);
        }
        catch (Exception ex)
        {
            SetMonitorMetric("cpu", "Нет данных", ex.Message, 0);
        }
    }

    private sealed class MonitorMetricUi
    {
        public TextBlock Value { get; }
        public TextBlock Details { get; }
        public Border Track { get; }
        public Border Fill { get; }
        public double Percent { get; set; }

        public MonitorMetricUi(TextBlock value, TextBlock details, Border track, Border fill)
        {
            Value = value;
            Details = details;
            Track = track;
            Fill = fill;
        }
    }

    // ----------------------------- ГЛОБАЛЬНЫЕ ГОРЯЧИЕ КЛАВИШИ -----------------------------

    private void AddHotkeySettings(StackPanel content)
    {
        content.Children.Add(MakeText("ГОРЯЧИЕ КЛАВИШИ", 11, Brush("#64D8CB"), FontWeights.SemiBold));
        var card = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14), Margin = new Thickness(0, 7, 0, 12)
        };
        var stack = new StackPanel();
        stack.Children.Add(MakeText("Сочетания клавиш", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        stack.Children.Add(MakeText("Нажми на поле и введи сочетание, например Ctrl + Alt + N. Все привязки пустые по умолчанию. Одно сочетание нельзя назначить двум действиям.", 10, Brush("#91A1B9")));

        foreach ((string id, string label) in HotkeyActionDefinitions)
        {
            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = MakeText(label, 11, Brush("#AAB6CA"));
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);

            _appData.Settings.Hotkeys.TryGetValue(id, out string? currentBinding);
            var field = new TextBox
            {
                Text = currentBinding ?? string.Empty,
                Tag = id,
                IsReadOnly = true,
                Height = 34,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(6, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = LocalizationService.Translate("Нажми здесь, затем нажми сочетание клавиш")
            };
            field.PreviewKeyDown += HotkeyCapture_PreviewKeyDown;
            field.GotKeyboardFocus += (_, _) => field.SelectAll();
            Grid.SetColumn(field, 1);
            row.Children.Add(field);

            var clear = MakeButton("×", (_, _) => ClearHotkeyBinding(id, field));
            clear.Width = 32;
            clear.Height = 32;
            clear.Padding = new Thickness(0);
            clear.Margin = new Thickness(0);
            clear.ToolTip = LocalizationService.Translate("Очистить сочетание");
            Grid.SetColumn(clear, 2);
            row.Children.Add(clear);
            stack.Children.Add(row);
        }

        card.Child = stack;
        content.Children.Add(card);
    }

    private void HotkeyCapture_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox field || field.Tag is not string actionId) return;
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        ModifierKeys modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            ShowNotice("Нужен модификатор", "Используй сочетание как минимум с одной клавишей Ctrl, Alt, Shift или Win.");
            return;
        }

        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        string binding = string.Join("+", parts);

        string? duplicateAction = _appData.Settings.Hotkeys
            .Where(pair => pair.Key != actionId && !string.IsNullOrWhiteSpace(pair.Value))
            .FirstOrDefault(pair => TryParseHotkey(pair.Value, out uint existingModifiers, out uint existingKey) &&
                existingModifiers == ToNativeModifiers(modifiers) && existingKey == (uint)KeyInterop.VirtualKeyFromKey(key)).Key;
        if (duplicateAction is not null)
        {
            string duplicateName = HotkeyActionDefinitions.FirstOrDefault(item => item.Id == duplicateAction).Label ?? duplicateAction;
            ShowNotice("Сочетание уже используется", $"{binding} уже назначено действию «{duplicateName}». Выбери другое сочетание.");
            return;
        }

        _appData.Settings.Hotkeys[actionId] = binding;
        field.Text = binding;
        SaveData();
        RefreshGlobalHotkeys(showErrors: true);
    }

    private void ClearHotkeyBinding(string actionId, TextBox field)
    {
        _appData.Settings.Hotkeys.Remove(actionId);
        field.Text = string.Empty;
        SaveData();
        RefreshGlobalHotkeys(showErrors: false);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(HotkeyWindowProc);
        RefreshGlobalHotkeys(showErrors: false);
    }

    private void RefreshGlobalHotkeys(bool showErrors)
    {
        if (_windowSource is null) return;
        foreach (int id in _registeredHotkeys.Keys.ToArray())
            UnregisterHotKey(_windowSource.Handle, id);
        _registeredHotkeys.Clear();

        List<string> errors = new();
        foreach (var binding in _appData.Settings.Hotkeys.ToArray())
        {
            if (string.IsNullOrWhiteSpace(binding.Value)) continue;
            if (!TryParseHotkey(binding.Value, out uint modifiers, out uint virtualKey))
            {
                errors.Add($"{binding.Key}: {binding.Value}");
                continue;
            }

            int id = _nextHotkeyId++;
            if (RegisterHotKey(_windowSource.Handle, id, modifiers | 0x4000u, virtualKey))
                _registeredHotkeys[id] = binding.Key;
            else
                errors.Add($"{binding.Value} — Windows сообщает, что сочетание занято другим приложением.");
        }

        if (showErrors && errors.Count > 0)
            ShowNotice("Не все сочетания удалось зарегистрировать", string.Join(Environment.NewLine, errors));
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        uint native = 0;
        if ((modifiers & ModifierKeys.Control) != 0) native |= 0x0002;
        if ((modifiers & ModifierKeys.Alt) != 0) native |= 0x0001;
        if ((modifiers & ModifierKeys.Shift) != 0) native |= 0x0004;
        if ((modifiers & ModifierKeys.Windows) != 0) native |= 0x0008;
        return native;
    }

    private static string FormatHotkeyBinding(uint modifiers, uint virtualKey)
    {
        var parts = new List<string>();
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        Key key = KeyInterop.KeyFromVirtualKey((int)virtualKey);
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private static bool TryParseHotkey(string binding, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;
        string[] parts = binding.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        string keyName = parts[^1];
        foreach (string modifier in parts[..^1])
        {
            switch (modifier.ToLowerInvariant())
            {
                case "ctrl": modifiers |= 0x0002; break;
                case "alt": modifiers |= 0x0001; break;
                case "shift": modifiers |= 0x0004; break;
                case "win": modifiers |= 0x0008; break;
                default: return false;
            }
        }
        if (modifiers == 0 || !Enum.TryParse(keyName, true, out Key key) || key == Key.None) return false;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0) return false;
        virtualKey = (uint)vk;
        return true;
    }

    private IntPtr HotkeyWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && _registeredHotkeys.TryGetValue(wParam.ToInt32(), out string? action))
        {
            handled = true;
            Dispatcher.BeginInvoke(new Action(() => ExecuteHotkeyAction(action)));
        }
        return IntPtr.Zero;
    }

    private void ExecuteHotkeyAction(string action)
    {
        switch (action)
        {
            case "TogglePanel":
                if (!IsVisible) RestoreFromTray();
                if (_isExpanded) CollapseToBubble(); else ExpandPanel();
                break;
            case "QuickActions":
                ShowQuickActions();
                break;
            case "Clock":
            case "Notes":
            case "Calculator":
            case "Converter":
            case "Reminders":
            case "Pomodoro":
            case "Calendar":
            case "Monitor":
                ShowAndNavigate(action);
                break;
        }
    }

    private void InitializeQuickActionsMenu()
    {
        _quickActionsMenu = new ContextMenu
        {
            Background = Brush("#121B2B"),
            Foreground = Brush("#F3F6FC"),
            BorderBrush = Brush("#35445A"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5)
        };
        AddQuickActionMenuItem("◷", "Часы", () => ShowAndNavigate("Clock"));
        AddQuickActionMenuItem("▤", "Заметки", () => ShowAndNavigate("Notes"));
        AddQuickActionMenuItem("＋", "Калькулятор", () => ShowAndNavigate("Calculator"));
        AddQuickActionMenuItem("⇄", "Конвертер", () => ShowAndNavigate("Converter"));
        AddQuickActionMenuItem("●", "Напоминания", () => ShowAndNavigate("Reminders"));
        AddQuickActionMenuItem("◉", "Pomodoro", () => ShowAndNavigate("Pomodoro"));
        AddQuickActionMenuItem("▦", "Календарь", () => ShowAndNavigate("Calendar"));
        AddQuickActionMenuItem("▥", "Мониторинг компьютера", () => ShowAndNavigate("Monitor"));
        _quickActionsMenu.Items.Add(new Separator());
        AddQuickActionMenuItem("↓", "Скрыть в системный трей", HideToTray);
        AddQuickActionMenuItem("×", "Выйти из Orbiq", RequestClose);
        Bubble.ContextMenu = _quickActionsMenu;
        Bubble.ToolTip = LocalizationService.Translate("ЛКМ — открыть Orbiq · ПКМ — быстрые действия");
    }

    private void AddQuickActionMenuItem(string glyph, string text, Action action)
    {
        if (_quickActionsMenu is null) return;
        var header = new Grid { MinWidth = 232 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = MakeText(glyph, 15, Brush("#64D8CB"), FontWeights.SemiBold);
        icon.VerticalAlignment = VerticalAlignment.Center;
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(icon);
        var label = MakeText(text, 12, Brush("#F3F6FC"));
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0);
        Grid.SetColumn(label, 1);
        header.Children.Add(label);

        var item = new MenuItem
        {
            Header = header,
            Foreground = Brush("#F3F6FC"),
            Background = Brushes.Transparent,
            Padding = new Thickness(10, 8, 12, 8),
            Margin = new Thickness(1, 1, 1, 1),
            FontSize = 12,
            StaysOpenOnClick = false
        };
        item.Click += (_, _) => action();
        _quickActionsMenu.Items.Add(item);
    }

    private void ShowQuickActions()
    {
        if (!IsVisible) RestoreFromTray();
        if (_quickActionsMenu is null) InitializeQuickActionsMenu();
        if (_isExpanded)
        {
            _quickActionsMenu!.PlacementTarget = Panel;
        }
        else
        {
            Bubble.ContextMenu = _quickActionsMenu;
            _quickActionsMenu!.PlacementTarget = Bubble;
        }
        _quickActionsMenu!.IsOpen = true;
    }

    private void UnregisterGlobalHotkeys()
    {
        if (_windowSource is null) return;
        foreach (int id in _registeredHotkeys.Keys.ToArray())
            UnregisterHotKey(_windowSource.Handle, id);
        _registeredHotkeys.Clear();
        _windowSource.RemoveHook(HotkeyWindowProc);
        _windowSource = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ----------------------------- НАСТРОЙКИ И ТЕМЫ -----------------------------

    private UIElement BuildSettingsPage()
    {
        _paletteInputs.Clear();
        var root = new DockPanel();
        var heading = new StackPanel { Margin = new Thickness(2, 0, 0, 14) };
        heading.Children.Add(MakeText("Настройки", 23, Brush("#FFFFFF"), FontWeights.SemiBold));
        heading.Children.Add(MakeText("Оформление, звук и системные параметры Orbiq", 12, Brush("#AAB6CA")));
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        // Горизонтальная прокрутка отключена: карточки занимают всю доступную ширину.
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false
        };
        var content = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };

        // ----------------------------- ИНТЕРФЕЙС -----------------------------
        content.Children.Add(MakeText("ИНТЕРФЕЙС", 11, Brush("#64D8CB"), FontWeights.SemiBold));
        var interfaceCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14), Margin = new Thickness(0, 7, 0, 12)
        };
        var interfaceStack = new StackPanel();

        // Язык интерфейса — коды языков сохраняются независимо от отображаемого названия.
        interfaceStack.Children.Add(MakeText("Язык интерфейса", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var languageCombo = new ComboBox
        {
            Height = 38, FontSize = 13, Foreground = Brush("#FFFFFF"), Background = Brush("#26364D"),
            BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1), Padding = new Thickness(9, 6, 9, 6),
            Margin = new Thickness(0, 3, 0, 10), SelectedValuePath = "Tag"
        };
        foreach (var language in LocalizationService.Languages)
            languageCombo.Items.Add(new ComboBoxItem { Content = language.DisplayName, Tag = language.Code });
        languageCombo.SelectedValue = _appData.Settings.LanguageCode;
        languageCombo.SelectionChanged += (_, _) =>
        {
            if (languageCombo.SelectedValue is not string code || code == _appData.Settings.LanguageCode) return;
            _appData.Settings.LanguageCode = LocalizationService.NormalizeCode(code);
            LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
            SaveData();
            ApplyShellLocalization();
            InitializeQuickActionsMenu();
            NavigateTo("Settings");
        };
        interfaceStack.Children.Add(languageCombo);
        interfaceStack.Children.Add(MakeText("Выбери язык для всех разделов и элементов управления Orbiq.", 10, Brush("#91A1B9")));
        interfaceStack.Children.Add(new Border { Height = 1, Background = Brush("#35445A"), Margin = new Thickness(0, 13, 0, 11) });

        // Приветствие
        interfaceStack.Children.Add(MakeText("Приветствие в главном окне", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        interfaceStack.Children.Add(MakeText("Текст отображается в верхней части Orbiq.", 10, Brush("#91A1B9")));
        var greetingRow = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        greetingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        greetingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var greetingInput = new TextBox
        {
            Text = string.Equals(_appData.Settings.WelcomeText, "Приятного использования", StringComparison.Ordinal)
                ? LocalizationService.Translate("Приятного использования") : _appData.Settings.WelcomeText,
            MaxLength = 80,
            Height = 38,
            Padding = new Thickness(9, 7, 9, 7),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 8, 2)
        };
        greetingRow.Children.Add(greetingInput);
        var greetingSave = MakeButton("Сохранить", (_, _) =>
        {
            string value = greetingInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                ShowNotice("Пустое приветствие", "Введи текст приветствия или напиши «Приятного использования».");
                return;
            }
            _appData.Settings.WelcomeText = value;
            ApplyWelcomeText();
            SaveData();
            ShowNotice("Приветствие изменено", "Новый текст будет показываться при каждом запуске Orbiq.");
        });
        greetingSave.Margin = new Thickness(0, 2, 0, 2);
        Grid.SetColumn(greetingSave, 1);
        greetingRow.Children.Add(greetingSave);
        interfaceStack.Children.Add(greetingRow);

        interfaceStack.Children.Add(new Border { Height = 1, Background = Brush("#35445A"), Margin = new Thickness(0, 13, 0, 11) });

        // Тема
        interfaceStack.Children.Add(MakeText("Тема интерфейса", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var themeCombo = MakeCombo(new[] { "Чёрная", "Тёмная", "Светлая", "Голубая", "Своя палитра" });
        themeCombo.SelectedItem = _appData.Settings.Theme;
        themeCombo.SelectionChanged += (_, _) =>
        {
            if (themeCombo.SelectedItem is not string selected) return;
            _appData.Settings.Theme = selected;
            ApplyTheme();
            SaveData();
            NavigateTo("Settings");
        };
        interfaceStack.Children.Add(themeCombo);
        interfaceStack.Children.Add(MakeText("Цветовая схема применяется ко всем основным разделам.", 10, Brush("#91A1B9")));

        // Своя палитра находится сразу под выбором темы и существует только для соответствующего режима.
        bool customPaletteSelected = string.Equals(_appData.Settings.Theme, "Своя палитра", StringComparison.Ordinal);
        if (customPaletteSelected)
        {
            var customPaletteCard = new Border
            {
                Background = Brush("#192438"), CornerRadius = new CornerRadius(12),
                BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1),
                Padding = new Thickness(12), Margin = new Thickness(0, 8, 0, 4)
            };
            var customStack = new StackPanel();
            customStack.Children.Add(MakeText("СВОЯ ПАЛИТРА", 12, Brush("#64D8CB"), FontWeights.SemiBold));
            customStack.Children.Add(MakeText("Настрой цвета элементов Orbiq и нажми кнопку применения.", 10, Brush("#91A1B9")));
            AddPaletteInput(customStack, "Фон окна", "CustomWindow", _appData.Settings.CustomWindow);
            AddPaletteInput(customStack, "Основная панель", "CustomPanel", _appData.Settings.CustomPanel);
            AddPaletteInput(customStack, "Карточки", "CustomCard", _appData.Settings.CustomCard);
            AddPaletteInput(customStack, "Кнопки и поля", "CustomButton", _appData.Settings.CustomButton);
            AddPaletteInput(customStack, "Акцентный цвет", "CustomAccent", _appData.Settings.CustomAccent);
            AddPaletteInput(customStack, "Основной текст", "CustomText", _appData.Settings.CustomText);
            AddPaletteInput(customStack, "Вторичный текст", "CustomMuted", _appData.Settings.CustomMuted);
            AddPaletteInput(customStack, "Границы", "CustomBorder", _appData.Settings.CustomBorder);
            var applyPalette = MakeButton("Применить свою палитру", (_, _) => ApplyCustomPalette(), "#22766F");
            applyPalette.HorizontalAlignment = HorizontalAlignment.Left;
            customStack.Children.Add(applyPalette);
            customPaletteCard.Child = customStack;
            interfaceStack.Children.Add(customPaletteCard);
        }

        interfaceStack.Children.Add(new Border { Height = 1, Background = Brush("#35445A"), Margin = new Thickness(0, 13, 0, 11) });

        // Громкость расположена непосредственно над выбором звука напоминаний.
        interfaceStack.Children.Add(MakeText("Громкость сигнала", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var volumeHeader = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        volumeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volumeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volumeHeader.Children.Add(MakeText("Громкость уведомлений Orbiq", 10, Brush("#91A1B9")));
        var volumeValue = MakeText($"{Math.Clamp(_appData.Settings.NotificationVolume, 0, 100):0}%", 11, Brush("#64D8CB"), FontWeights.SemiBold);
        volumeValue.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(volumeValue, 1);
        volumeHeader.Children.Add(volumeValue);
        interfaceStack.Children.Add(volumeHeader);

        var volumeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_appData.Settings.NotificationVolume, 0, 100),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            SmallChange = 1,
            LargeChange = 5,
            Margin = new Thickness(2, 2, 2, 11),
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center
        };
        volumeSlider.ValueChanged += (_, args) =>
        {
            _appData.Settings.NotificationVolume = Math.Clamp(args.NewValue, 0, 100);
            volumeValue.Text = $"{_appData.Settings.NotificationVolume:0}%";
            SaveData();
        };
        interfaceStack.Children.Add(volumeSlider);

        interfaceStack.Children.Add(MakeText("Звук напоминаний", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var soundRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        soundRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var soundCombo = MakeCombo(new[] { "Мягкий сигнал", "Короткий сигнал", "Свой звук", "Без звука" });
        soundCombo.SelectedItem = _appData.Settings.NotificationSound;
        soundRow.Children.Add(soundCombo);
        var testSound = MakeButton("Проверить", (_, _) => PlayNotificationSound(showPlaybackError: true));
        testSound.Margin = new Thickness(8, 3, 0, 5);
        Grid.SetColumn(testSound, 1);
        soundRow.Children.Add(testSound);
        interfaceStack.Children.Add(soundRow);

        var customSoundPanel = new Border
        {
            Background = Brush("#121B2B"), BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(11), Margin = new Thickness(0, 7, 0, 0),
            Visibility = _appData.Settings.NotificationSound == "Свой звук" ? Visibility.Visible : Visibility.Collapsed
        };
        var customSoundStack = new StackPanel();
        customSoundStack.Children.Add(MakeText("СВОЙ АУДИОФАЙЛ", 11, Brush("#64D8CB"), FontWeights.SemiBold));
        customSoundStack.Children.Add(MakeText("Перетащи файл в область ниже или выбери его на компьютере. Принимаются WAV, MP3 и WMA до 30 МБ. Проверяется содержимое файла, а не только его название.", 10, Brush("#91A1B9")));
        string? currentSoundPath = _dataService.ResolveStoredAssetPath(_appData.Settings.CustomReminderSoundPath);
        var customSoundStatus = MakeText(currentSoundPath is null ? "Файл ещё не выбран" : $"Выбран: {Path.GetFileName(currentSoundPath)}", 11, Brush("#AAB6CA"));
        customSoundStatus.Margin = new Thickness(0, 6, 0, 5);
        customSoundStack.Children.Add(customSoundStatus);
        var soundDropZone = new Border
        {
            Background = Brush("#192438"), BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9), Padding = new Thickness(10), MinHeight = 56, AllowDrop = true
        };
        soundDropZone.Child = MakeText("Перетащи сюда аудиофайл", 11, Brush("#AAB6CA"), FontWeights.Medium);
        soundDropZone.DragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            args.Handled = true;
        };
        soundDropZone.Drop += (_, args) =>
        {
            if (args.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                ImportCustomReminderSound(files[0], customSoundStatus);
            args.Handled = true;
        };
        customSoundStack.Children.Add(soundDropZone);
        var customSoundButtons = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var chooseSoundButton = MakeButton("Выбрать файл", (_, _) => ChooseCustomReminderSound(customSoundStatus));
        chooseSoundButton.Margin = new Thickness(0, 2, 6, 2);
        customSoundButtons.Children.Add(chooseSoundButton);
        var removeSoundButton = MakeButton("Убрать свой звук", (_, _) =>
        {
            string? oldAsset = _appData.Settings.CustomReminderSoundPath;
            string oldSoundMode = _appData.Settings.NotificationSound;
            _appData.Settings.CustomReminderSoundPath = string.Empty;
            _appData.Settings.NotificationSound = "Мягкий сигнал";
            if (!SaveData())
            {
                _appData.Settings.CustomReminderSoundPath = oldAsset ?? string.Empty;
                _appData.Settings.NotificationSound = oldSoundMode;
                return;
            }
            _dataService.DeleteStoredAsset(oldAsset);
            NavigateTo("Settings");
        }, "#573346");
        removeSoundButton.Margin = new Thickness(0, 2, 0, 2);
        customSoundButtons.Children.Add(removeSoundButton);
        customSoundStack.Children.Add(customSoundButtons);
        customSoundPanel.Child = customSoundStack;
        interfaceStack.Children.Add(customSoundPanel);

        soundCombo.SelectionChanged += (_, _) =>
        {
            if (soundCombo.SelectedItem is string selected)
            {
                _appData.Settings.NotificationSound = selected;
                customSoundPanel.Visibility = selected == "Свой звук" ? Visibility.Visible : Visibility.Collapsed;
                SaveData();
            }
        };
        interfaceStack.Children.Add(MakeText("Настройка громкости применяется ко всем звукам напоминаний.", 10, Brush("#91A1B9")));

        interfaceStack.Children.Add(new Border { Height = 1, Background = Brush("#35445A"), Margin = new Thickness(0, 13, 0, 11) });
        AddBubbleIconSettings(interfaceStack);

        interfaceCard.Child = interfaceStack;
        content.Children.Add(interfaceCard);

        AddHotkeySettings(content);

        // ----------------------------- СИСТЕМА -----------------------------
        content.Children.Add(MakeText("СИСТЕМА", 11, Brush("#64D8CB"), FontWeights.SemiBold));

        // 1. Автозагрузка
        var startupCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 7, 0, 12) };
        var startupStack = new StackPanel();
        startupStack.Children.Add(MakeText("Запуск вместе с Windows", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        var startupCheck = new CheckBox
        {
            Content = LocalizationService.Translate("Запускать Orbiq при входе в Windows"),
            IsChecked = StartupManager.IsEnabled(),
            Foreground = Brush("#F3F6FC"),
            FontSize = 12,
            Margin = new Thickness(0, 9, 0, 5)
        };
        startupCheck.Checked += (_, _) => SetStartupEnabled(true, startupCheck);
        startupCheck.Unchecked += (_, _) => SetStartupEnabled(false, startupCheck);
        startupStack.Children.Add(startupCheck);
        startupStack.Children.Add(MakeText("По умолчанию выключено. Переключатель связан с автозагрузкой Windows и может также управляться через Диспетчер задач → Автозагрузка приложений.", 10, Brush("#91A1B9")));
        startupCard.Child = startupStack;
        content.Children.Add(startupCard);

        // 2. Папка сохранений
        var storageCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
        var storageStack = new StackPanel();
        storageStack.Children.Add(MakeText("Папка сохранений", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        storageStack.Children.Add(MakeText("Заметки, напоминания и настройки Orbiq хранятся отдельно от файлов приложения.", 10, Brush("#91A1B9")));
        var storagePathText = MakeText(_dataService.DataDirectory, 11, Brush("#AAB6CA"));
        storagePathText.TextWrapping = TextWrapping.Wrap;
        storagePathText.Margin = new Thickness(0, 7, 0, 8);
        storageStack.Children.Add(storagePathText);
        var changeStorageButton = MakeButton("Изменить папку сохранений", (_, _) => ChangeStorageFolder());
        changeStorageButton.HorizontalAlignment = HorizontalAlignment.Left;
        storageStack.Children.Add(changeStorageButton);
        storageCard.Child = storageStack;
        content.Children.Add(storageCard);

        // 3. Резервные копии — рядом с выбором папки сохранений.
        AddBackupSettings(content);

        // Сброс пользовательских параметров без изменения путей данных и резервных копий.
        var resetCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
        var resetStack = new StackPanel();
        resetStack.Children.Add(MakeText("Настройки по умолчанию", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        resetStack.Children.Add(MakeText("Вернуть стандартное приветствие, тему, громкость, размер кружка и остальные параметры. Папка сохранений, папка резервных копий и сами данные останутся без изменений.", 10, Brush("#91A1B9")));
        var resetSettingsButton = MakeButton("Сбросить настройки по умолчанию", (_, _) => PromptResetSettings(), "#30435C");
        resetSettingsButton.HorizontalAlignment = HorizontalAlignment.Left;
        resetSettingsButton.Margin = new Thickness(0, 9, 0, 0);
        resetStack.Children.Add(resetSettingsButton);
        resetCard.Child = resetStack;
        content.Children.Add(resetCard);

        // Удаление программы — последний пункт системного блока.
        var uninstallCard = new Border { Background = Brush("#192438"), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12) };
        var uninstallStack = new StackPanel();
        uninstallStack.Children.Add(MakeText("Удаление программы", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        uninstallStack.Children.Add(MakeText("Можно удалить файлы приложения, сохранив заметки, или удалить приложение вместе с данными. При запуске из VS Code исходники проекта останутся на месте.", 10, Brush("#91A1B9")));
        var uninstallButton = MakeButton("Удалить Orbiq…", (_, _) => ShowUninstallOptions(), "#573346");
        uninstallButton.HorizontalAlignment = HorizontalAlignment.Left;
        uninstallButton.Margin = new Thickness(0, 9, 0, 0);
        uninstallStack.Children.Add(uninstallButton);
        uninstallCard.Child = uninstallStack;
        content.Children.Add(uninstallCard);

        scroll.Content = content;
        root.Children.Add(scroll);
        return root;
    }

    private void AddBackupSettings(StackPanel content)
    {
        var backupCard = new Border
        {
            Background = Brush("#192438"), CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12)
        };
        var stack = new StackPanel();
        stack.Children.Add(MakeText("Резервные копии", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        stack.Children.Add(MakeText("Orbiq может автоматически создавать архив сохранений раз в день. В копию входят заметки, напоминания, календарь, настройки и пользовательские файлы.", 10, Brush("#91A1B9")));

        string displayedPath = string.IsNullOrWhiteSpace(_appData.Settings.BackupDirectory)
            ? _dataService.DefaultBackupDirectory : _appData.Settings.BackupDirectory;
        var backupPathText = MakeText(displayedPath, 10, Brush("#AAB6CA"));
        backupPathText.TextWrapping = TextWrapping.Wrap;
        backupPathText.Margin = new Thickness(0, 2, 0, 8);

        var enabled = new CheckBox
        {
            Content = LocalizationService.Translate("Включить автоматические резервные копии"),
            IsChecked = _appData.Settings.AutomaticBackupsEnabled,
            Foreground = Brush("#F3F6FC"), FontSize = 12,
            Margin = new Thickness(0, 8, 0, 7)
        };
        enabled.Checked += (_, _) =>
        {
            try
            {
                string target = string.IsNullOrWhiteSpace(_appData.Settings.BackupDirectory)
                    ? _dataService.DefaultBackupDirectory : _appData.Settings.BackupDirectory;
                _dataService.ValidateBackupDirectory(target);
                Directory.CreateDirectory(target);
                _appData.Settings.BackupDirectory = Path.GetFullPath(target);
                _appData.Settings.AutomaticBackupsEnabled = true;
                backupPathText.Text = _appData.Settings.BackupDirectory;
                SaveData();
            }
            catch (Exception ex)
            {
                enabled.IsChecked = false;
                _appData.Settings.AutomaticBackupsEnabled = false;
                ShowNotice("Не удалось включить резервирование", ex.Message);
            }
        };
        enabled.Unchecked += (_, _) =>
        {
            _appData.Settings.AutomaticBackupsEnabled = false;
            SaveData();
        };
        stack.Children.Add(enabled);

        stack.Children.Add(backupPathText);

        var buttons = new WrapPanel();
        var chooseFolder = MakeButton("Выбрать папку", (_, _) =>
        {
            string? selected = PickBackupFolder();
            if (selected is null) return;
            try
            {
                _dataService.ValidateBackupDirectory(selected);
                Directory.CreateDirectory(selected);
                _appData.Settings.BackupDirectory = Path.GetFullPath(selected);
                backupPathText.Text = _appData.Settings.BackupDirectory;
                SaveData();
            }
            catch (Exception ex) { ShowNotice("Неподходящая папка", ex.Message); }
        });
        chooseFolder.Margin = new Thickness(0, 2, 6, 2);
        buttons.Children.Add(chooseFolder);

        var createBackup = MakeButton("Создать копию сейчас", (_, _) =>
        {
            try
            {
                string target = string.IsNullOrWhiteSpace(_appData.Settings.BackupDirectory)
                    ? _dataService.DefaultBackupDirectory : _appData.Settings.BackupDirectory;
                _dataService.ValidateBackupDirectory(target);
                if (!SaveData()) return;
                string archive = _dataService.CreateBackup(target, "Manual");
                string archiveMessage = LocalizationService.Translate("Архив сохранён сюда: {path}")
                    .Replace("{path}", archive, StringComparison.Ordinal);
                ShowNotice("Резервная копия создана", archiveMessage);
            }
            catch (Exception ex) { ShowNotice("Не удалось создать копию", ex.Message); }
        }, "#22766F");
        createBackup.Margin = new Thickness(0, 2, 6, 2);
        buttons.Children.Add(createBackup);

        var restoreBackup = MakeButton("Восстановить из копии…", (_, _) => RestoreBackupFromFile());
        restoreBackup.Margin = new Thickness(0, 2, 6, 2);
        buttons.Children.Add(restoreBackup);

        var openFolder = MakeButton("Открыть папку", (_, _) =>
        {
            try
            {
                string target = string.IsNullOrWhiteSpace(_appData.Settings.BackupDirectory)
                    ? _dataService.DefaultBackupDirectory : _appData.Settings.BackupDirectory;
                Directory.CreateDirectory(target);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex) { ShowNotice("Не удалось открыть папку", ex.Message); }
        });
        openFolder.Margin = new Thickness(0, 2, 0, 2);
        buttons.Children.Add(openFolder);
        stack.Children.Add(buttons);
        stack.Children.Add(MakeText("Автоматическая копия создаётся не чаще одного раза в сутки при сохранении данных. Хранится до 20 архивов Orbiq.", 10, Brush("#91A1B9")));
        backupCard.Child = stack;
        content.Children.Add(backupCard);
    }

    private void RestoreBackupFromFile()
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = LocalizationService.Translate("Выбери резервную копию Orbiq"),
            Filter = $"{LocalizationService.Translate("Архив Orbiq (*.zip)")}|*.zip|{LocalizationService.Translate("Все файлы")} (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = Directory.Exists(_appData.Settings.BackupDirectory)
                ? _appData.Settings.BackupDirectory : _dataService.DefaultBackupDirectory
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        // Диалог освобождается при выходе из метода; сохраняем выбранные значения
        // до показа асинхронной собственной плашки подтверждения.
        string selectedArchivePath = dialog.FileName;
        string backupDirectory = _appData.Settings.BackupDirectory;

        ShowConfirm("Восстановить резервную копию?",
            "Текущие заметки, настройки, напоминания и календарь будут заменены содержимым выбранного архива. Перед восстановлением Orbiq попробует создать страховочную копию текущих данных.", () =>
            {
                try
                {
                    string safetyCopy = _dataService.RestoreBackup(selectedArchivePath, backupDirectory);
                    _appData = _dataService.Load();
                    NormalizeAppData();
                    LocalizationService.SetLanguage(_appData.Settings.LanguageCode);
                    ApplyTheme();
                    ApplyShellLocalization();
                    ApplyWelcomeText();
                    ApplyBubbleSize(reposition: true);
                    ApplyBubbleAppearance();
                    try { StartupManager.SetEnabled(_appData.Settings.StartWithWindows); }
                    catch { /* Не блокируем восстановление, если Windows запрещает изменить автозагрузку. */ }
                    SynchronizeStartupSetting();
                    RefreshGlobalHotkeys(showErrors: false);
                    NavigateTo("Clock");
                    string suffix = string.IsNullOrWhiteSpace(safetyCopy)
                        ? LocalizationService.Translate("Страховочную копию создать не удалось, но восстановление завершено.")
                        : LocalizationService.Translate("Текущие данные дополнительно сохранены: {path}")
                            .Replace("{path}", safetyCopy, StringComparison.Ordinal);
                    string restoreMessage = LocalizationService.Translate("Данные Orbiq восстановлены из выбранного архива. {details}")
                        .Replace("{details}", suffix, StringComparison.Ordinal);
                    ShowNotice("Восстановление завершено", restoreMessage);
                }
                catch (Exception ex)
                {
                    ShowNotice("Не удалось восстановить копию", ex.Message);
                }
            });
    }

    private string? PickBackupFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Выбери отдельную папку для резервных копий Orbiq.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_appData.Settings.BackupDirectory)
                ? _appData.Settings.BackupDirectory : _dataService.DefaultBackupDirectory
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    private void ChooseCustomReminderSound(TextBlock statusText)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Title = LocalizationService.Translate("Выбери звук напоминания"),
            Filter = $"{LocalizationService.Translate("Аудиофайлы WAV, MP3, WMA")}|*.wav;*.mp3;*.wma|{LocalizationService.Translate("Все файлы")}|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            ImportCustomReminderSound(dialog.FileName, statusText);
    }

    private void ImportCustomReminderSound(string sourcePath, TextBlock statusText)
    {
        string validationError = MediaAssetValidator.ValidateAudioFile(sourcePath);
        if (!string.IsNullOrEmpty(validationError))
        {
            ShowNotice("Файл не подходит", validationError);
            return;
        }

        try
        {
            string? oldAsset = _appData.Settings.CustomReminderSoundPath;
            string oldSoundMode = _appData.Settings.NotificationSound;
            string newAsset = _dataService.StoreAssetFile(sourcePath, "reminder-sound");
            _appData.Settings.CustomReminderSoundPath = newAsset;
            _appData.Settings.NotificationSound = "Свой звук";
            if (!SaveData())
            {
                _appData.Settings.CustomReminderSoundPath = oldAsset ?? string.Empty;
                _appData.Settings.NotificationSound = oldSoundMode;
                _dataService.DeleteStoredAsset(newAsset);
                return;
            }
            if (!string.Equals(oldAsset, newAsset, StringComparison.OrdinalIgnoreCase))
                _dataService.DeleteStoredAsset(oldAsset);
            statusText.Text = LocalizationService.Translate("Выбран: {file}")
                .Replace("{file}", Path.GetFileName(sourcePath), StringComparison.Ordinal);
            ShowNotice("Пользовательский звук добавлен", "Файл проверен по своей структуре и сохранён в папке данных Orbiq. Он не запускается как программа — используется только для воспроизведения.");
        }
        catch (Exception ex)
        {
            ShowNotice("Не удалось добавить звук", ex.Message);
        }
    }

    private void AddBubbleIconSettings(StackPanel parent)
    {
        parent.Children.Add(MakeText("Иконка плавающего кружка", 14, Brush("#FFFFFF"), FontWeights.SemiBold));
        parent.Children.Add(MakeText("Добавь PNG, JPG, BMP или GIF. GIF будет проигрываться по кругу. Перетаскивай изображение внутри круга, чтобы выбрать кадрирование.", 10, Brush("#91A1B9")));
        parent.Children.Add(MakeText("Размер кружка можно увеличить до 220 px. На небольшом экране максимум автоматически ограничивается рабочей областью.", 10, Brush("#91A1B9")));
        var imageLimitsText = MakeText(GetImageLimitDescription(), 10, Brush("#91A1B9"));
        imageLimitsText.Margin = new Thickness(0, 3, 0, 0);
        parent.Children.Add(imageLimitsText);

        var sizeHeader = new Grid { Margin = new Thickness(0, 9, 0, 2) };
        sizeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        sizeHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sizeHeader.Children.Add(MakeText("Размер плавающего кружка", 11, Brush("#AAB6CA")));
        double initialSizePercent = BubbleSizeToPercent(GetBubbleSize());
        var sizeValueText = MakeText($"{initialSizePercent:0}% · {Math.Round(GetBubbleSize())} px", 11, Brush("#64D8CB"), FontWeights.SemiBold);
        Grid.SetColumn(sizeValueText, 1);
        sizeHeader.Children.Add(sizeValueText);
        parent.Children.Add(sizeHeader);
        var bubbleSizeSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = initialSizePercent,
            SmallChange = 1,
            LargeChange = 5,
            TickFrequency = 1,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 7),
            Height = 26,
            ToolTip = LocalizationService.Translate("Размер кружка в процентах от минимального до максимально допустимого для рабочего стола.")
        };
        void CommitBubbleSize()
        {
            _appData.Settings.BubbleSize = BubblePercentToSize(bubbleSizeSlider.Value);
            SaveData();
        }
        bubbleSizeSlider.ValueChanged += (_, _) =>
        {
            _appData.Settings.BubbleSize = BubblePercentToSize(bubbleSizeSlider.Value);
            sizeValueText.Text = $"{bubbleSizeSlider.Value:0}% · {Math.Round(_appData.Settings.BubbleSize)} px";
            imageLimitsText.Text = LocalizationService.Translate(GetImageLimitDescription());
            ApplyBubbleSize();
        };
        bubbleSizeSlider.PreviewMouseLeftButtonUp += (_, _) => CommitBubbleSize();
        bubbleSizeSlider.KeyUp += (_, _) => CommitBubbleSize();
        bubbleSizeSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => CommitBubbleSize()));
        parent.Children.Add(bubbleSizeSlider);

        var editorCard = new Border
        {
            Background = Brush("#121B2B"), BorderBrush = Brush("#35445A"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 7, 0, 0)
        };
        var editorStack = new StackPanel();

        string? pendingImagePath = _dataService.ResolveStoredAssetPath(_appData.Settings.CustomBubbleImagePath);
        BitmapSource[] pendingFrames = Array.Empty<BitmapSource>();
        int[] pendingDelays = Array.Empty<int>();
        string existingPath = pendingImagePath ?? string.Empty;

        var previewImage = new Image
        {
            Width = 116, Height = 116, Stretch = Stretch.UniformToFill,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var previewScale = new ScaleTransform(1, 1);
        var previewOffset = new TranslateTransform();
        previewImage.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection { previewScale, previewOffset }
        };
        var previewGlyph = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Orbiq;component/Assets/orbiq-mark.png", UriKind.Absolute)),
            Width = 102, Height = 102, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var previewGrid = new Grid { Width = 116, Height = 116, ClipToBounds = true };
        previewGrid.Clip = new EllipseGeometry(new Rect(0, 0, 116, 116));
        previewImage.Clip = null;
        previewGrid.Children.Add(previewGlyph);
        previewGrid.Children.Add(previewImage);
        var previewHost = new Border
        {
            Width = 118, Height = 118, CornerRadius = new CornerRadius(59),
            Background = Brush("#192438"), BorderBrush = Brush("#64D8CB"), BorderThickness = new Thickness(1.5),
            Child = previewGrid, AllowDrop = true, Cursor = Cursors.SizeAll,
            ToolTip = LocalizationService.Translate("Перетаскивай картинку мышью, чтобы настроить кадрирование")
        };

        var previewAndDetails = new Grid { Margin = new Thickness(0, 7, 0, 5) };
        previewAndDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        previewAndDetails.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        previewAndDetails.Children.Add(previewHost);
        var previewDetails = new StackPanel { Margin = new Thickness(14, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        previewDetails.Children.Add(MakeText("Предпросмотр", 12, Brush("#FFFFFF"), FontWeights.SemiBold));
        previewDetails.Children.Add(MakeText("В круге будет отображаться именно эта область изображения. GIF в самом плавающем значке будет анимирован.", 10, Brush("#91A1B9")));
        var imageStatus = MakeText("Стандартная иконка Orbiq", 10, Brush("#64D8CB"));
        imageStatus.Margin = new Thickness(0, 5, 0, 0);
        previewDetails.Children.Add(imageStatus);
        Grid.SetColumn(previewDetails, 1);
        previewAndDetails.Children.Add(previewDetails);
        editorStack.Children.Add(previewAndDetails);

        double initialZoom = Math.Max(1.0, _appData.Settings.CustomBubbleZoom);
        double initialZoomMaximum = initialZoom <= double.MaxValue / 2.0
            ? Math.Max(8.0, initialZoom * 2.0)
            : double.MaxValue;
        var zoomSlider = new Slider
        {
            Minimum = 1, Maximum = initialZoomMaximum, Value = initialZoom,
            SmallChange = Math.Max(0.05, initialZoom * 0.01),
            LargeChange = Math.Max(0.25, initialZoom * 0.05),
            Margin = new Thickness(0, 1, 0, 5), Height = 24,
            ToolTip = LocalizationService.Translate("Верхнего предела нет: при приближении к правому краю диапазон ползунка расширяется автоматически.")
        };
        var zoomValueText = MakeText($"Увеличение: {initialZoom:0.##}× · без верхнего предела", 10, Brush("#AAB6CA"));
        var offsetXSlider = new Slider
        {
            Minimum = -100, Maximum = 100, Value = _appData.Settings.CustomBubbleOffsetX,
            SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 1, 0, 5), Height = 24
        };
        var offsetYSlider = new Slider
        {
            Minimum = -100, Maximum = 100, Value = _appData.Settings.CustomBubbleOffsetY,
            SmallChange = 1, LargeChange = 10, Margin = new Thickness(0, 1, 0, 5), Height = 24
        };

        void RenderPreview()
        {
            previewScale.ScaleX = zoomSlider.Value;
            previewScale.ScaleY = zoomSlider.Value;
            previewOffset.X = offsetXSlider.Value * 0.52;
            previewOffset.Y = offsetYSlider.Value * 0.52;
        }

        void SetPendingImage(string path)
        {
            if (!TryLoadImageFrames(path, out BitmapSource[] frames, out int[] delays, out string error))
            {
                ShowNotice("Изображение не подходит", error);
                return;
            }
            pendingImagePath = path;
            pendingFrames = frames;
            pendingDelays = delays;
            previewImage.Source = pendingFrames[0];
            previewImage.Visibility = Visibility.Visible;
            previewGlyph.Visibility = Visibility.Collapsed;
            imageStatus.Text = LocalizationService.Translate("Выбрано: {file}")
                .Replace("{file}", Path.GetFileName(path), StringComparison.Ordinal);
            RenderPreview();
        }

        if (!string.IsNullOrWhiteSpace(existingPath) && TryLoadImageFrames(existingPath, out pendingFrames, out pendingDelays, out _))
        {
            previewImage.Source = pendingFrames[0];
            imageStatus.Text = LocalizationService.Translate("Текущая иконка: {file}")
                .Replace("{file}", Path.GetFileName(existingPath), StringComparison.Ordinal);
            previewGlyph.Visibility = Visibility.Collapsed;
        }
        else
        {
            pendingImagePath = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewGlyph.Visibility = Visibility.Visible;
        }
        RenderPreview();

        bool isDraggingPreview = false;
        Point dragStart = new();
        double dragStartX = 0;
        double dragStartY = 0;
        previewHost.MouseLeftButtonDown += (_, args) =>
        {
            if (pendingImagePath is null) return;
            isDraggingPreview = true;
            dragStart = args.GetPosition(previewHost);
            dragStartX = offsetXSlider.Value;
            dragStartY = offsetYSlider.Value;
            previewHost.CaptureMouse();
            args.Handled = true;
        };
        previewHost.MouseMove += (_, args) =>
        {
            if (!isDraggingPreview || args.LeftButton != MouseButtonState.Pressed) return;
            Point current = args.GetPosition(previewHost);
            double sensitivity = 100.0 / (previewHost.Width * 0.45);
            offsetXSlider.Value = Math.Clamp(dragStartX + (current.X - dragStart.X) * sensitivity, -100, 100);
            offsetYSlider.Value = Math.Clamp(dragStartY + (current.Y - dragStart.Y) * sensitivity, -100, 100);
            RenderPreview();
        };
        previewHost.MouseLeftButtonUp += (_, _) =>
        {
            isDraggingPreview = false;
            previewHost.ReleaseMouseCapture();
        };

        previewHost.DragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            args.Handled = true;
        };
        previewHost.Drop += (_, args) =>
        {
            if (args.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                SetPendingImage(files[0]);
            args.Handled = true;
        };

        zoomSlider.ValueChanged += (_, _) =>
        {
            // Ползунок расширяет диапазон по мере приближения к текущему максимуму.
            // Это позволяет пользоваться им без заранее заданного предела масштаба.
            if (zoomSlider.Value >= zoomSlider.Maximum * 0.9 && zoomSlider.Maximum < double.MaxValue)
            {
                zoomSlider.Maximum = zoomSlider.Maximum <= double.MaxValue / 2.0
                    ? zoomSlider.Maximum * 2.0
                    : double.MaxValue;
            }

            string zoomText = zoomSlider.Value >= 1000
                ? zoomSlider.Value.ToString("0.###E+0", LocalizationService.CurrentCulture)
                : zoomSlider.Value.ToString("0.##", LocalizationService.CurrentCulture);
            zoomValueText.Text = LocalizationService.Translate("Увеличение: {zoom}× · без верхнего предела")
                .Replace("{zoom}", zoomText, StringComparison.Ordinal);
            RenderPreview();
        };
        offsetXSlider.ValueChanged += (_, _) => RenderPreview();
        offsetYSlider.ValueChanged += (_, _) => RenderPreview();

        editorStack.Children.Add(zoomValueText);
        editorStack.Children.Add(zoomSlider);
        var offsetLabels = new Grid();
        offsetLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        offsetLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        offsetLabels.Children.Add(MakeText("Сдвиг по горизонтали", 10, Brush("#AAB6CA")));
        var verticalLabel = MakeText("Сдвиг по вертикали", 10, Brush("#AAB6CA"));
        Grid.SetColumn(verticalLabel, 1);
        offsetLabels.Children.Add(verticalLabel);
        editorStack.Children.Add(offsetLabels);
        var offsetSliders = new Grid();
        offsetSliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        offsetSliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        offsetSliders.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        offsetSliders.Children.Add(offsetXSlider);
        Grid.SetColumn(offsetYSlider, 1);
        offsetSliders.Children.Add(offsetYSlider);
        editorStack.Children.Add(offsetSliders);

        var iconButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var chooseIconButton = MakeButton("Выбрать картинку / GIF", (_, _) =>
        {
            using var dialog = new Forms.OpenFileDialog
            {
                Title = LocalizationService.Translate("Выбери изображение для плавающего кружка"),
                Filter = $"{LocalizationService.Translate("Изображения и GIF")}|*.png;*.jpg;*.jpeg;*.bmp;*.gif|{LocalizationService.Translate("Все файлы")}|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() == Forms.DialogResult.OK)
                SetPendingImage(dialog.FileName);
        });
        chooseIconButton.Margin = new Thickness(0, 2, 6, 2);
        iconButtons.Children.Add(chooseIconButton);

        var applyIconButton = MakeButton("Применить иконку", (_, _) =>
        {
            if (pendingImagePath is null || pendingFrames.Length == 0)
            {
                ShowNotice("Нет изображения", "Сначала выбери картинку или GIF для плавающего кружка.");
                return;
            }
            try
            {
                string? oldAsset = _appData.Settings.CustomBubbleImagePath;
                double oldZoom = _appData.Settings.CustomBubbleZoom;
                double oldOffsetX = _appData.Settings.CustomBubbleOffsetX;
                double oldOffsetY = _appData.Settings.CustomBubbleOffsetY;
                string newAsset = _dataService.StoreAssetFile(pendingImagePath, "bubble-icon");
                _appData.Settings.CustomBubbleImagePath = newAsset;
                _appData.Settings.CustomBubbleZoom = zoomSlider.Value;
                _appData.Settings.CustomBubbleOffsetX = offsetXSlider.Value;
                _appData.Settings.CustomBubbleOffsetY = offsetYSlider.Value;
                if (!SaveData())
                {
                    _appData.Settings.CustomBubbleImagePath = oldAsset ?? string.Empty;
                    _appData.Settings.CustomBubbleZoom = oldZoom;
                    _appData.Settings.CustomBubbleOffsetX = oldOffsetX;
                    _appData.Settings.CustomBubbleOffsetY = oldOffsetY;
                    _dataService.DeleteStoredAsset(newAsset);
                    return;
                }
                ApplyBubbleAppearance();
                if (!string.Equals(oldAsset, newAsset, StringComparison.OrdinalIgnoreCase))
                    _dataService.DeleteStoredAsset(oldAsset);
                imageStatus.Text = LocalizationService.Translate("Применено: {file}")
                    .Replace("{file}", Path.GetFileName(pendingImagePath), StringComparison.Ordinal);
                ShowNotice("Иконка обновлена", "Новая картинка будет отображаться в плавающем кружке. Если выбран GIF, его кадры будут проигрываться по кругу.");
            }
            catch (Exception ex)
            {
                ShowNotice("Не удалось применить иконку", ex.Message);
            }
        }, "#22766F");
        applyIconButton.Margin = new Thickness(0, 2, 6, 2);
        iconButtons.Children.Add(applyIconButton);

        var resetIconButton = MakeButton("Вернуть стандартную", (_, _) =>
        {
            string? oldAsset = _appData.Settings.CustomBubbleImagePath;
            double oldZoom = _appData.Settings.CustomBubbleZoom;
            double oldOffsetX = _appData.Settings.CustomBubbleOffsetX;
            double oldOffsetY = _appData.Settings.CustomBubbleOffsetY;
            _appData.Settings.CustomBubbleImagePath = string.Empty;
            _appData.Settings.CustomBubbleZoom = 1;
            _appData.Settings.CustomBubbleOffsetX = 0;
            _appData.Settings.CustomBubbleOffsetY = 0;
            if (!SaveData())
            {
                _appData.Settings.CustomBubbleImagePath = oldAsset ?? string.Empty;
                _appData.Settings.CustomBubbleZoom = oldZoom;
                _appData.Settings.CustomBubbleOffsetX = oldOffsetX;
                _appData.Settings.CustomBubbleOffsetY = oldOffsetY;
                return;
            }
            ApplyBubbleAppearance();
            _dataService.DeleteStoredAsset(oldAsset);
            pendingImagePath = null;
            pendingFrames = Array.Empty<BitmapSource>();
            previewImage.Source = null;
            previewImage.Visibility = Visibility.Collapsed;
            previewGlyph.Visibility = Visibility.Visible;
            zoomSlider.Value = 1;
            offsetXSlider.Value = 0;
            offsetYSlider.Value = 0;
            imageStatus.Text = LocalizationService.Translate("Стандартная иконка Orbiq");
            RenderPreview();
        }, "#573346");
        resetIconButton.Margin = new Thickness(0, 2, 0, 2);
        iconButtons.Children.Add(resetIconButton);
        editorStack.Children.Add(iconButtons);

        editorCard.Child = editorStack;
        parent.Children.Add(editorCard);
    }

    private void AddPaletteInput(System.Windows.Controls.Panel parent, string label, string key, string value)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        var text = MakeText(label, 11, Brush("#AAB6CA"));
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var input = new TextBox { Text = value, Height = 31, Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        Grid.SetColumn(input, 1);
        grid.Children.Add(input);
        _paletteInputs[key] = input;
        parent.Children.Add(grid);
    }

    private void ApplyCustomPalette()
    {
        string Get(string key) => _paletteInputs.TryGetValue(key, out TextBox? input) ? input.Text.Trim() : string.Empty;
        var values = new Dictionary<string, string>
        {
            ["CustomWindow"] = Get("CustomWindow"), ["CustomPanel"] = Get("CustomPanel"),
            ["CustomCard"] = Get("CustomCard"), ["CustomButton"] = Get("CustomButton"),
            ["CustomAccent"] = Get("CustomAccent"), ["CustomText"] = Get("CustomText"),
            ["CustomMuted"] = Get("CustomMuted"), ["CustomBorder"] = Get("CustomBorder")
        };
        foreach (var item in values)
        {
            if (!IsValidColor(item.Value))
            {
                string colorError = LocalizationService.Translate("В поле «{field}» указан неверный цвет. Используй формат #RRGGBB.")
                    .Replace("{field}", item.Key, StringComparison.Ordinal);
                ShowNotice("Неправильный цвет", colorError);
                return;
            }
        }
        _appData.Settings.CustomWindow = values["CustomWindow"];
        _appData.Settings.CustomPanel = values["CustomPanel"];
        _appData.Settings.CustomCard = values["CustomCard"];
        _appData.Settings.CustomButton = values["CustomButton"];
        _appData.Settings.CustomAccent = values["CustomAccent"];
        _appData.Settings.CustomText = values["CustomText"];
        _appData.Settings.CustomMuted = values["CustomMuted"];
        _appData.Settings.CustomBorder = values["CustomBorder"];
        _appData.Settings.Theme = "Своя палитра";
        ApplyTheme();
        SaveData();
        NavigateTo("Settings");
    }

    private static bool IsValidColor(string value)
    {
        try
        {
            if (!value.StartsWith("#", StringComparison.Ordinal) || (value.Length != 7 && value.Length != 9)) return false;
            _ = ColorConverter.ConvertFromString(value);
            return true;
        }
        catch { return false; }
    }

    private void ApplyTheme()
    {
        AppSettings settings = _appData.Settings;
        _palette = settings.Theme switch
        {
            "Чёрная" => ThemePalette.Black,
            "Светлая" => ThemePalette.Light,
            "Голубая" => ThemePalette.Blue,
            "Своя палитра" => new ThemePalette(settings.CustomWindow, settings.CustomPanel, settings.CustomCard, settings.CustomButton,
                CreateHoverColor(settings.CustomButton), settings.CustomAccent, settings.CustomText, settings.CustomMuted, settings.CustomBorder, "#6A3444"),
            _ => ThemePalette.Dark
        };
        SetResourceBrush("WindowBrush", _palette.Window);
        SetResourceBrush("PanelBrush", _palette.Panel);
        SetResourceBrush("BubbleBrush", _palette.Window);
        SetResourceBrush("CardBrush", _palette.Card);
        SetResourceBrush("InputBrush", _palette.Button);
        SetResourceBrush("ButtonBrush", _palette.Button);
        SetResourceBrush("ButtonHoverBrush", _palette.ButtonHover);
        SetResourceBrush("AccentBrush", _palette.Accent);
        SetResourceBrush("TextBrush", _palette.Text);
        SetResourceBrush("MutedBrush", _palette.Muted);
        SetResourceBrush("BorderBrush", _palette.Border);
        SetResourceBrush("DangerBrush", _palette.Danger);
        if (_quickActionsMenu is not null) InitializeQuickActionsMenu();
    }

    private static string CreateHoverColor(string hex)
    {
        Color color = (Color)ColorConverter.ConvertFromString(hex);
        byte Lift(byte channel) => (byte)Math.Clamp(channel + (255 - channel) * 0.12, 0, 255);
        return $"#{color.A:X2}{Lift(color.R):X2}{Lift(color.G):X2}{Lift(color.B):X2}";
    }

    private static void SetResourceBrush(string key, string color)
    {
        if (Application.Current is not null)
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    private void PlayNotificationSound(bool showPlaybackError = false)
    {
        string sound = _appData.Settings.NotificationSound;
        double volume = Math.Clamp(_appData.Settings.NotificationVolume, 0, 100) / 100.0;
        if (sound == "Без звука" || volume <= 0) return;

        if (sound == "Свой звук")
        {
            string? customPath = _dataService.ResolveStoredAssetPath(_appData.Settings.CustomReminderSoundPath);
            if (customPath is null)
            {
                if (showPlaybackError)
                    ShowNotice("Свой звук не выбран", "Открой настройки Orbiq и выбери корректный аудиофайл WAV, MP3 или WMA.");
                return;
            }
            PlayCustomReminderAudio(customPath, volume, showPlaybackError);
            return;
        }

        byte[] wave = sound == "Короткий сигнал"
            ? CreateShortSignalWave(volume)
            : CreateSoftChimeWave(volume);

        _ = Task.Run(() =>
        {
            try { _ = PlayWave(wave, IntPtr.Zero, 0x0004); } // SND_MEMORY
            catch { /* Звук не должен мешать работе приложения. */ }
        });
    }

    private void PlayCustomReminderAudio(string path, double volume, bool showPlaybackError)
    {
        try
        {
            if (_customReminderPlayer is not null)
            {
                _customReminderPlayer.Stop();
                _customReminderPlayer.Close();
                _customReminderPlayer = null;
            }

            var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
            _customReminderPlayer = player;
            player.MediaOpened += (_, _) =>
            {
                if (ReferenceEquals(_customReminderPlayer, player))
                    player.Play();
            };
            player.MediaEnded += (_, _) =>
            {
                player.Stop();
                player.Close();
                if (ReferenceEquals(_customReminderPlayer, player))
                    _customReminderPlayer = null;
            };
            player.MediaFailed += (_, _) =>
            {
                player.Close();
                if (ReferenceEquals(_customReminderPlayer, player))
                    _customReminderPlayer = null;
                if (showPlaybackError)
                    Dispatcher.BeginInvoke(new Action(() => ShowNotice("Не удалось воспроизвести звук", "Windows не смогла декодировать этот файл. Попробуй другой MP3 или WAV-файл.")));
            };
            player.Open(new Uri(path, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            try { _customReminderPlayer?.Close(); } catch { }
            _customReminderPlayer = null;
            if (showPlaybackError)
                ShowNotice("Не удалось воспроизвести звук", ex.Message);
        }
    }

    [DllImport("winmm.dll", EntryPoint = "PlaySound", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlayWave([In] byte[] sound, IntPtr module, uint flags);

    private static byte[] CreateSoftChimeWave(double volume)
    {
        const int sampleRate = 22050;
        const double duration = 0.78;
        int sampleCount = (int)(sampleRate * duration);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteWaveHeader(writer, sampleRate, sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double fadeIn = Math.Min(1.0, t / 0.025);
            double fadeOut = Math.Min(1.0, (duration - t) / 0.22);
            double envelope = Math.Max(0, Math.Min(fadeIn, fadeOut));
            double tone = Math.Sin(2 * Math.PI * 660 * t) * 0.62 + Math.Sin(2 * Math.PI * 880 * t) * 0.28;
            short sample = (short)(tone * envelope * 0.30 * volume * short.MaxValue);
            writer.Write(sample);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateShortSignalWave(double volume)
    {
        const int sampleRate = 22050;
        const double duration = 0.24;
        int sampleCount = (int)(sampleRate * duration);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        WriteWaveHeader(writer, sampleRate, sampleCount);
        for (int i = 0; i < sampleCount; i++)
        {
            double t = (double)i / sampleRate;
            double fadeIn = Math.Min(1.0, t / 0.008);
            double fadeOut = Math.Min(1.0, (duration - t) / 0.09);
            double envelope = Math.Max(0, Math.Min(fadeIn, fadeOut));
            double tone = Math.Sin(2 * Math.PI * 880 * t);
            short sample = (short)(tone * envelope * 0.24 * volume * short.MaxValue);
            writer.Write(sample);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteWaveHeader(BinaryWriter writer, int sampleRate, int sampleCount)
    {
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + sampleCount * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(sampleCount * 2);
    }

    private sealed record ThemePalette(string Window, string Panel, string Card, string Button, string ButtonHover,
        string Accent, string Text, string Muted, string Border, string Danger)
    {
        public static ThemePalette Dark { get; } = new("#101827", "#121B2B", "#192438", "#26364D", "#30435C", "#64D8CB", "#F3F6FC", "#AAB6CA", "#35445A", "#573346");
        public static ThemePalette Black { get; } = new("#07090E", "#0D111A", "#141A25", "#202938", "#2B394D", "#69D8CA", "#F4F6FB", "#A1AABC", "#303A4A", "#603044");
        public static ThemePalette Light { get; } = new("#E9EEF5", "#F5F7FB", "#FFFFFF", "#DCE5F0", "#C8D6E6", "#168B83", "#1C2738", "#58677D", "#CDD7E4", "#A84A60");
        public static ThemePalette Blue { get; } = new("#061A2E", "#0A223B", "#103152", "#17466C", "#205982", "#65B9FF", "#EAF5FF", "#A9C2D9", "#285477", "#74465A");
    }

    // ----------------------------- UI-ПОМОЩНИКИ -----------------------------

    private static TextBlock MakeText(string text, double size, Brush foreground, FontWeight? weight = null, int maxLines = 0, bool translate = true)
    {
        var block = new TextBlock
        {
            Text = translate ? LocalizationService.Translate(text) : text, FontSize = size, Foreground = foreground,
            FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        if (maxLines > 0) block.MaxHeight = size * 1.5 * maxLines;
        return block;
    }

    private Brush Brush(string hex)
    {
        string key = hex.ToUpperInvariant();
        string color = key switch
        {
            "#F0121B2B" or "#121B2B" => _palette.Panel,
            "#192438" => _palette.Panel,
            "#1B2A40" or "#243B4D" => _palette.Card,
            "#26364D" or "#202F45" => _palette.Button,
            "#30435C" => _palette.ButtonHover,
            "#64D8CB" or "#22766F" => _palette.Accent,
            "#AAB6CA" or "#91A1B9" or "#71819A" => _palette.Muted,
            "#F3F6FC" or "#F4F7FC" or "#EAF0F8" or "#FFFFFF" => _palette.Text,
            "#35445A" or "#354862" or "#40516A" or "#33445B" => _palette.Border,
            "#573346" or "#6A3444" => _palette.Danger,
            _ => hex
        };
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
        catch { return Brushes.White; }
    }

    private Button MakeButton(string text, RoutedEventHandler onClick, string background = "#26364D")
    {
        var button = new Button
        {
            Content = LocalizationService.Translate(text), Foreground = Brush("#FFFFFF"), Background = Brush(background),
            BorderThickness = new Thickness(0), Padding = new Thickness(13, 9, 13, 9),
            Margin = new Thickness(4), FontSize = 13, Cursor = Cursors.Hand, Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Click += onClick;
        button.Template = MakeRoundedButtonTemplate();
        return button;
    }

    private ControlTemplate MakeRoundedButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "ButtonSurface" };
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,
            new DynamicResourceExtension("ButtonHoverBrush"), "ButtonSurface"));
        template.Triggers.Add(hover);

        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty,
            new DynamicResourceExtension("ButtonBrush"), "ButtonSurface"));
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
        template.Triggers.Add(pressed);

        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
        template.Triggers.Add(disabled);

        return template;
    }

    private void Bubble_MouseEnter(object sender, MouseEventArgs e)
    {
        Bubble.Background = Application.Current.Resources["ButtonHoverBrush"] as Brush ?? Brush("#30435C");
        Bubble.BorderBrush = Application.Current.Resources["AccentBrush"] as Brush ?? Brush("#64D8CB");
        Bubble.Opacity = 1;
        _bubbleGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0.72, TimeSpan.FromMilliseconds(140)));
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)));
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(140)));
    }

    private void Bubble_MouseLeave(object sender, MouseEventArgs e)
    {
        Bubble.Background = Application.Current.Resources["BubbleBrush"] as Brush ?? Brush("#192438");
        Bubble.BorderBrush = Application.Current.Resources["BorderBrush"] as Brush ?? Brush("#35445A");
        Bubble.Opacity = 0.82;
        _bubbleGlow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(140)));
        BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }
}
