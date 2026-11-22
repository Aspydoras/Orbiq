using System;

namespace Orbiq.Models;

public sealed class NoteItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Новая заметка";
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Color { get; set; } = "#1B2A40";
    public bool IsPinned { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
