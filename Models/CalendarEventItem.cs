using System;

namespace Orbiq.Models;

public sealed class CalendarEventItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Новое событие";
    public DateTime Date { get; set; } = DateTime.Today;
    public string Time { get; set; } = "09:00";
    public string Details { get; set; } = string.Empty;
    public string LastTriggeredMinute { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}
