using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TodoApp.Models;

namespace TodoApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();
    public DbSet<TodoCategory> Categories => Set<TodoCategory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<TodoItem>(e =>
        {
            e.HasIndex(t => t.UserId);
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.DueDate);
            e.HasOne(t => t.Category)
                .WithMany(c => c.Todos)
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<TodoCategory>(e =>
        {
            e.HasIndex(c => c.UserId);
        });
    }
}
