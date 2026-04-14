using System.ComponentModel.DataAnnotations;

namespace TodoApp.Models;

public class TodoItem
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    public TodoPriority Priority { get; set; } = TodoPriority.Medium;

    public TodoStatus Status { get; set; } = TodoStatus.Pending;

    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    public int? CategoryId { get; set; }
    public TodoCategory? Category { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;
}

public enum TodoPriority
{
    Low,
    Medium,
    High,
    Urgent
}

public enum TodoStatus
{
    Pending,
    InProgress,
    Completed
}
