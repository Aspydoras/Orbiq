using System;
using System.Collections.Generic;

namespace Orbiq.Models;

public sealed class ReminderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "Новое напоминание";
    public string Time { get; set; } = "20:00";
    public bool IsEnabled { get; set; } = true;
    // Однократное напоминание: дата следующего наступления выбранного времени.
    // Поле OneTimeDate сохранено с прежним именем ради совместимости с сохранениями.
    public bool IsOneTime { get; set; }
    public DateTime? OneTimeDate { get; set; }
    // Пустой список означает ежедневно — это сохраняет поведение старых версий.
    // 1 = понедельник, 7 = воскресенье.
    public List<int> RepeatDays { get; set; } = new();
    public DateTime? SnoozeUntil { get; set; }
    public string LastTriggeredDate { get; set; } = string.Empty;
}
