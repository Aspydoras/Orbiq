using System.Collections.Generic;

namespace Orbiq.Models;

public sealed class AppSettings
{
    public string LanguageCode { get; set; } = "EN";
    public string Theme { get; set; } = "Тёмная";
    public string NotificationSound { get; set; } = "Мягкий сигнал";
    public double NotificationVolume { get; set; } = 50;
    public string CustomReminderSoundPath { get; set; } = string.Empty;
    public string CustomBubbleImagePath { get; set; } = string.Empty;
    public double BubbleSize { get; set; } = 132;
    public double CustomBubbleZoom { get; set; } = 1.0;
    public double CustomBubbleOffsetX { get; set; }
    public double CustomBubbleOffsetY { get; set; }
    public string WelcomeText { get; set; } = "Приятного использования";
    public bool StartWithWindows { get; set; }

    // Версия миграции логики одноразовых напоминаний (дата автоматически вычисляется по ближайшему времени).
    public int OneTimeReminderScheduleVersion { get; set; }

    // Горячие клавиши: ключ — действие, значение — сочетание вроде Ctrl+Alt+N.
    // Пустой словарь по умолчанию означает, что глобальные бинды не назначены.
    public Dictionary<string, string> Hotkeys { get; set; } = new();

    // Pomodoro
    public int PomodoroWorkMinutes { get; set; } = 25;
    public int PomodoroShortBreakMinutes { get; set; } = 5;
    public int PomodoroLongBreakMinutes { get; set; } = 15;
    public int PomodoroCyclesBeforeLongBreak { get; set; } = 4;
    public int PomodoroCompletedSessions { get; set; }

    // Калькулятор: история и память сохраняются вместе с остальными настройками.
    public List<string> CalculatorHistory { get; set; } = new();
    public double CalculatorMemory { get; set; }

    // Резервные копии
    public bool AutomaticBackupsEnabled { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public string LastAutomaticBackupDate { get; set; } = string.Empty;

    public string CustomWindow { get; set; } = "#101827";
    public string CustomPanel { get; set; } = "#121B2B";
    public string CustomCard { get; set; } = "#192438";
    public string CustomButton { get; set; } = "#26364D";
    public string CustomAccent { get; set; } = "#64D8CB";
    public string CustomText { get; set; } = "#F3F6FC";
    public string CustomMuted { get; set; } = "#AAB6CA";
    public string CustomBorder { get; set; } = "#35445A";
}
