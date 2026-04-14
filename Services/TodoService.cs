using Microsoft.EntityFrameworkCore;
using TodoApp.Data;
using TodoApp.Models;

namespace TodoApp.Services;

public class TodoService(ApplicationDbContext db) : ITodoService
{
    public async Task<List<TodoItem>> GetTodosAsync(string userId, TodoFilter? filter = null)
    {
        var query = db.Todos
            .Include(t => t.Category)
            .Where(t => t.UserId == userId);

        if (filter is not null)
        {
            if (filter.Status.HasValue)
                query = query.Where(t => t.Status == filter.Status.Value);

            if (filter.Priority.HasValue)
                query = query.Where(t => t.Priority == filter.Priority.Value);

            if (filter.CategoryId.HasValue)
                query = query.Where(t => t.CategoryId == filter.CategoryId.Value);

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
                query = query.Where(t =>
                    t.Title.Contains(filter.SearchTerm) ||
                    (t.Description != null && t.Description.Contains(filter.SearchTerm)));

            if (filter.OverdueOnly == true)
                query = query.Where(t => t.DueDate < DateTime.UtcNow && t.Status != TodoStatus.Completed);

            query = filter.SortBy switch
            {
                "Priority" => filter.SortDescending
                    ? query.OrderByDescending(t => t.Priority)
                    : query.OrderBy(t => t.Priority),
                "CreatedAt" => filter.SortDescending
                    ? query.OrderByDescending(t => t.CreatedAt)
                    : query.OrderBy(t => t.CreatedAt),
                "Title" => filter.SortDescending
                    ? query.OrderByDescending(t => t.Title)
                    : query.OrderBy(t => t.Title),
                _ => filter.SortDescending
                    ? query.OrderByDescending(t => t.DueDate)
                    : query.OrderBy(t => t.DueDate.HasValue ? 0 : 1).ThenBy(t => t.DueDate)
            };
        }
        else
        {
            query = query.OrderBy(t => t.Status)
                         .ThenBy(t => t.DueDate.HasValue ? 0 : 1)
                         .ThenBy(t => t.DueDate);
        }

        return await query.ToListAsync();
    }

    public async Task<TodoItem?> GetTodoAsync(int id, string userId)
    {
        return await db.Todos
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    }

    public async Task<TodoItem> CreateTodoAsync(TodoItem todo)
    {
        todo.CreatedAt = DateTime.UtcNow;
        db.Todos.Add(todo);
        await db.SaveChangesAsync();
        return todo;
    }

    public async Task<TodoItem> UpdateTodoAsync(TodoItem todo)
    {
        var existing = await db.Todos.FirstOrDefaultAsync(t => t.Id == todo.Id && t.UserId == todo.UserId)
            ?? throw new InvalidOperationException("Todo not found.");

        existing.Title = todo.Title;
        existing.Description = todo.Description;
        existing.Priority = todo.Priority;
        existing.DueDate = todo.DueDate;
        existing.CategoryId = todo.CategoryId;

        if (existing.Status != TodoStatus.Completed && todo.Status == TodoStatus.Completed)
            existing.CompletedAt = DateTime.UtcNow;
        else if (todo.Status != TodoStatus.Completed)
            existing.CompletedAt = null;

        existing.Status = todo.Status;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteTodoAsync(int id, string userId)
    {
        var todo = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        if (todo is not null)
        {
            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
        }
    }

    public async Task<List<TodoCategory>> GetCategoriesAsync(string userId)
    {
        return await db.Categories
            .Include(c => c.Todos)
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<TodoCategory> CreateCategoryAsync(TodoCategory category)
    {
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    public async Task<TodoCategory> UpdateCategoryAsync(TodoCategory category)
    {
        var existing = await db.Categories.FirstOrDefaultAsync(c => c.Id == category.Id && c.UserId == category.UserId)
            ?? throw new InvalidOperationException("Category not found.");

        existing.Name = category.Name;
        existing.Color = category.Color;

        await db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteCategoryAsync(int id, string userId)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
        if (category is not null)
        {
            db.Categories.Remove(category);
            await db.SaveChangesAsync();
        }
    }

    public async Task<TodoStats> GetStatsAsync(string userId)
    {
        var todos = await db.Todos.Where(t => t.UserId == userId).ToListAsync();
        var now = DateTime.UtcNow;
        var today = now.Date;

        return new TodoStats
        {
            Total = todos.Count,
            Pending = todos.Count(t => t.Status == TodoStatus.Pending),
            InProgress = todos.Count(t => t.Status == TodoStatus.InProgress),
            Completed = todos.Count(t => t.Status == TodoStatus.Completed),
            Overdue = todos.Count(t => t.DueDate < now && t.Status != TodoStatus.Completed),
            DueToday = todos.Count(t => t.DueDate?.Date == today && t.Status != TodoStatus.Completed)
        };
    }
}
