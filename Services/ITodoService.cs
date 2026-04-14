using TodoApp.Models;

namespace TodoApp.Services;

public interface ITodoService
{
    Task<List<TodoItem>> GetTodosAsync(string userId, TodoFilter? filter = null);
    Task<TodoItem?> GetTodoAsync(int id, string userId);
    Task<TodoItem> CreateTodoAsync(TodoItem todo);
    Task<TodoItem> UpdateTodoAsync(TodoItem todo);
    Task DeleteTodoAsync(int id, string userId);
    Task<List<TodoCategory>> GetCategoriesAsync(string userId);
    Task<TodoCategory> CreateCategoryAsync(TodoCategory category);
    Task<TodoCategory> UpdateCategoryAsync(TodoCategory category);
    Task DeleteCategoryAsync(int id, string userId);
    Task<TodoStats> GetStatsAsync(string userId);
}

public class TodoFilter
{
    public TodoStatus? Status { get; set; }
    public TodoPriority? Priority { get; set; }
    public int? CategoryId { get; set; }
    public string? SearchTerm { get; set; }
    public bool? OverdueOnly { get; set; }
    public string SortBy { get; set; } = "DueDate";
    public bool SortDescending { get; set; }
}

public class TodoStats
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Overdue { get; set; }
    public int DueToday { get; set; }
}
