using System.Collections.Generic;

namespace Orbiq.Models;

public sealed class AppData
{
    public List<NoteItem> Notes { get; set; } = new();
    public List<ReminderItem> Reminders { get; set; } = new();
    public List<CalendarEventItem> CalendarEvents { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
