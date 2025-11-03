using System.Text.Json;
using FlowFocus.Core.Models;
using FlowFocus.Core.Storage;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    private readonly string _dbPath;

    public StorageContext(string? dbPath = null)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(folder))
            folder = ".";
        _dbPath = Path.Combine(folder, dbPath ?? "flowfocus.db");
    }

    // Отдельные таблицы для каждой сущности
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskBlocker> TaskBlockers => Set<TaskBlocker>();
    public DbSet<UserAppSettings> Settings => Set<UserAppSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
        => options.UseSqlite($"Data Source={_dbPath}");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TaskItem
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Tags как JSON (проще чем CSV)
            entity.Property(e => e.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );

            // RepeatInfo как JSON
            entity.Property(e => e.Repeat)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<RepeatInfo>(v, (JsonSerializerOptions?)null)
                );

            // Индексы для частых запросов
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.AssignedDate);
            entity.HasIndex(e => e.IsFavorite);
        });

        // TaskBlocker
        modelBuilder.Entity<TaskBlocker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ParentTaskId);
            entity.HasIndex(e => e.BlockerTaskId);
        });

        // UserAppSettings (будет только одна запись)
        modelBuilder.Entity<UserAppSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DayStartTime).HasConversion(
                v => v.Ticks,
                v => TimeSpan.FromTicks(v)
            );
        });
    }
}

public class TaskRepository(StorageContext context) : ITaskRepository<TaskItem>
{
    public async Task<List<TaskItem>> GetAllAsync()
        => await context.Tasks.OrderBy(t => t.AssignedDate).ToListAsync();

    public async Task<TaskItem?> GetByIdAsync(int id)
        => await context.Tasks.FirstOrDefaultAsync(t => t.Id == id);

    public async Task AddAsync(TaskItem task)
    {
        context.Tasks.Add(task);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(TaskItem task)
    {
        context.Tasks.Update(task);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var task = await GetByIdAsync(id);
        if (task != null)
        {
            context.Tasks.Remove(task);
            await context.SaveChangesAsync();
        }
    }

    public async Task<List<TaskItem>> GetByStatusAsync(TodoTaskStatus status)
        => await context.Tasks.Where(t => t.Status == status).ToListAsync();

    public async Task<List<TaskItem>> GetByDateAsync(DateTime date)
        => await context.Tasks.Where(t => t.AssignedDate.HasValue &&
                                          t.AssignedDate.Value.Date == date.Date)
            .ToListAsync();
}