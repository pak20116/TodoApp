using System.ComponentModel.DataAnnotations;

namespace TodoApp.Models;

public class TodoCategory
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(7)]
    public string Color { get; set; } = "#3b82f6";

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ICollection<TodoItem> Todos { get; set; } = [];
}
