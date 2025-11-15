using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

public class DependencyRepository(AppDbContext context) : BaseRepository<Dependency>(context), IDependencyRepository
{
    public async Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(int taskId)
    {
        return await _context.Dependencies
            .Where(d => d.SourceTaskId == taskId)
            .Include(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<Dependency>> GetDependentTasksAsync(int taskId)
    {
        return await _context.Dependencies
            .Where(d => d.TargetTaskId == taskId)
            .Include(d => d.SourceTask)
            .ToListAsync();
    }

    public async Task<bool> HasCircularDependencyAsync(int sourceTaskId, int targetTaskId)
    {
        // Проверка прямой циклической зависимости
        if (await _context.Dependencies.AnyAsync(d =>
                d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId))
            return true;

        // Проверка транзитивных зависимостей через рекурсивный запрос
        return await CheckTransitiveDependencyAsync(targetTaskId, sourceTaskId, new HashSet<int>());
    }

    private async Task<bool> CheckTransitiveDependencyAsync(int currentTaskId, int targetTaskId, HashSet<int> visited)
    {
        if (!visited.Add(currentTaskId))
            return false;

        var dependencies = await _context.Dependencies
            .Where(d => d.SourceTaskId == currentTaskId)
            .Select(d => d.TargetTaskId)
            .ToListAsync();

        foreach (var dependencyId in dependencies)
        {
            if (dependencyId == targetTaskId)
                return true;

            if (await CheckTransitiveDependencyAsync(dependencyId, targetTaskId, visited))
                return true;
        }

        visited.Remove(currentTaskId);
        return false;
    }

    public async Task RemoveDependenciesForTaskAsync(int taskId)
    {
        var dependencies = await _context.Dependencies
            .Where(d => d.SourceTaskId == taskId || d.TargetTaskId == taskId)
            .ToListAsync();

        _context.Dependencies.RemoveRange(dependencies);
        await _context.SaveChangesAsync();
        InvalidateCache();
    }

    public async Task<IEnumerable<Dependency>> GetBlockingDependenciesAsync(int taskId)
    {
        return await _context.Dependencies
            .Where(d => d.SourceTaskId == taskId && d.Type == DependencyType.Blocking)
            .Include(d => d.TargetTask)
            .ToListAsync();
    }
}

public class SettingsRepository(AppDbContext context) : BaseRepository<UserSettings>(context), ISettingsRepository
{
    public async Task<UserSettings> GetUserSettingsAsync()
    {
        var settings = await _context.UserSettings.FirstOrDefaultAsync();
        if (settings == null)
        {
            settings = new UserSettings();
            await _context.UserSettings.AddAsync(settings);
            await _context.SaveChangesAsync();
        }

        return settings;
    }

    public async Task<bool> GetAutoRecalculateSettingAsync()
    {
        var settings = await GetUserSettingsAsync();
        return settings.AutoRecalculateOnAdd;
    }

    public async Task UpdateDayStartHourAsync(int hour)
    {
        var settings = await GetUserSettingsAsync();
        settings.DayStartHour = Math.Clamp(hour, 0, 23);
        await UpdateAsync(settings);
    }
}
public static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services,
        string connectionString = "Data Source=flowfocus.db")
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IDependencyRepository, DependencyRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        // Добавляем сервисы для работы с повторяющимися задачами
        services.AddScoped<IRecurringTaskService, RecurringTaskService>();
        services.AddScoped<INotificationService, NotificationService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.EnsureCreatedAsync();

        // Создаем начальные настройки, если их нет
        if (!await context.UserSettings.AnyAsync())
        {
            context.UserSettings.Add(new UserSettings());
            await context.SaveChangesAsync();
        }
    }
}

public class RecurringTaskService(AppDbContext context, ITaskRepository taskRepository) : IRecurringTaskService
{
    private readonly AppDbContext _context = context;

    public async Task ProcessRecurringTasksAsync()
    {
        var recurringTasks = await taskRepository.GetRecurringTasksAsync();
        var today = DateTime.Today;

        foreach (var task in recurringTasks.Where(t => t.Recurrence != null))
        {
            if (ShouldCreateRecurrence(task, today))
            {
                await CreateNextRecurrenceAsync(task);
            }
        }
    }

    public async Task CreateNextRecurrenceAsync(TaskItem task)
    {
        if (task.Recurrence == null) return;

        var nextDate = CalculateNextDate(task.Recurrence, DateTime.Today);
        if (nextDate.HasValue && (!task.RecurrenceEndDate.HasValue || nextDate <= task.RecurrenceEndDate))
        {
            var newTask = new TaskItem
            {
                Title = task.Title,
                Description = task.Description,
                UserPriority = task.UserPriority,
                Status = TaskStatus.Planned,
                Interest = task.Interest,
                Complexity = task.Complexity,
                EstimatedHours = task.EstimatedHours,
                PlannedDate = nextDate,
                IsRecurring = true,
                Recurrence = task.Recurrence,
                RecurrenceEndDate = task.RecurrenceEndDate,
                ParentTaskId = task.Id,
                Tags = new List<string>(task.Tags),
                DisplayType = task.DisplayType
            };

            await taskRepository.AddAsync(newTask);
        }
    }

    private bool ShouldCreateRecurrence(TaskItem task, DateTime today)
    {
        if (task.Recurrence == null) return false;

        var lastOccurrence = task.PlannedDate ?? task.CreatedDate.Date;
        var nextDate = CalculateNextDate(task.Recurrence, lastOccurrence);

        return nextDate.HasValue && nextDate.Value.Date <= today.Date;
    }

    private DateTime? CalculateNextDate(RecurrencePattern pattern, DateTime lastDate)
    {
        return pattern.Type switch
        {
            RecurrenceType.Daily => lastDate.AddDays(pattern.Interval),
            RecurrenceType.Weekly => CalculateNextWeeklyDate(pattern, lastDate),
            RecurrenceType.Monthly => CalculateNextMonthlyDate(pattern, lastDate),
            RecurrenceType.Yearly => lastDate.AddYears(pattern.Interval),
            _ => null
        };
    }

    private DateTime? CalculateNextWeeklyDate(RecurrencePattern pattern, DateTime lastDate)
    {
        var nextDate = lastDate.AddDays(7 * pattern.Interval);
        if (pattern.DaysOfWeek?.Any() == true)
        {
            // Поиск следующего подходящего дня недели
            for (int i = 0; i < 7; i++)
            {
                var candidate = nextDate.AddDays(i);
                if (pattern.DaysOfWeek.Contains(candidate.DayOfWeek))
                    return candidate;
            }
        }

        return nextDate;
    }

    private DateTime? CalculateNextMonthlyDate(RecurrencePattern pattern, DateTime lastDate)
    {
        var nextDate = lastDate.AddMonths(pattern.Interval);
        if (pattern.DayOfMonth.HasValue)
        {
            var daysInMonth = DateTime.DaysInMonth(nextDate.Year, nextDate.Month);
            var day = Math.Min(pattern.DayOfMonth.Value, daysInMonth);
            return new DateTime(nextDate.Year, nextDate.Month, day);
        }

        return nextDate;
    }
}

public abstract class BaseRepository<T>(AppDbContext context) : IRepository<T> where T : class
{
    protected readonly AppDbContext _context = context;

    protected readonly Microsoft.Extensions.Caching.Memory.IMemoryCache _cache =
        new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        var cacheKey = $"{typeof(T).Name}_{id}";
        if (_cache.TryGetValue(cacheKey, out T? cachedEntity))
            return cachedEntity;

        var entity = await _context.Set<T>().FindAsync(id);
        if (entity != null)
            _cache.Set(cacheKey, entity, TimeSpan.FromMinutes(5));

        return entity;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync()
    {
        var cacheKey = $"{typeof(T).Name}_All";
        if (_cache.TryGetValue(cacheKey, out IEnumerable<T>? cachedEntities))
            return cachedEntities ?? new List<T>();

        var entities = await _context.Set<T>().ToListAsync();
        _cache.Set(cacheKey, entities, TimeSpan.FromMinutes(2));

        return entities;
    }

    public virtual async Task AddAsync(T entity)
    {
        await _context.Set<T>().AddAsync(entity);
        await _context.SaveChangesAsync();
        InvalidateCache();
    }

    public virtual async Task UpdateAsync(T entity)
    {
        _context.Set<T>().Update(entity);
        await _context.SaveChangesAsync();
        InvalidateCache();
    }

    public virtual async Task DeleteAsync(int id)
    {
        var entity = await GetByIdAsync(id);
        if (entity != null)
        {
            _context.Set<T>().Remove(entity);
            await _context.SaveChangesAsync();
            InvalidateCache();
        }
    }

    public virtual async Task<bool> ExistsAsync(int id)
    {
        return await GetByIdAsync(id) != null;
    }

    protected virtual void InvalidateCache()
    {
        if (_cache is Microsoft.Extensions.Caching.Memory.MemoryCache memoryCache)
        {
            // Упрощенная инвалидация - в продакшене нужно более точное управление кэшем
            memoryCache.Compact(1.0);
        }
    }
}

public class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TaskItem> Tasks { get; set; }
    public DbSet<Dependency> Dependencies { get; set; }
    public DbSet<UserSettings> UserSettings { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=flowfocus.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // TaskItem configuration
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Title).IsRequired().HasMaxLength(500);
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.Tags).HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            );
            entity.Property(t => t.Status).HasConversion<string>();
            entity.Property(t => t.DisplayType).HasConversion<string>();

            // Рекуррентные задачи
            entity.Property(t => t.Recurrence)
                .HasConversion(
                    v => v == null
                        ? null
                        : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : System.Text.Json.JsonSerializer.Deserialize<RecurrencePattern>(v,
                            (System.Text.Json.JsonSerializerOptions?)null)
                );

            // Индексы
            entity.HasIndex(t => t.Status);
            entity.HasIndex(t => t.PlannedDate);
            entity.HasIndex(t => t.UserPriority);
            entity.HasIndex(t => t.ParentTaskId);
        });

        // Dependency configuration
        modelBuilder.Entity<Dependency>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Type).HasConversion<string>();
            entity.Property(d => d.Logic).HasConversion<string>();

            // Relationships
            entity.HasOne(d => d.SourceTask)
                .WithMany(t => t.Dependencies)
                .HasForeignKey(d => d.SourceTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.TargetTask)
                .WithMany(t => t.DependentTasks)
                .HasForeignKey(d => d.TargetTaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasCheckConstraint("CK_Dependency_SelfReference", "SourceTaskId != TargetTaskId");
        });

        // UserSettings configuration
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasData(new UserSettings { Id = 1 });
        });
    }
}

public class TaskRepository(AppDbContext context) : BaseRepository<TaskItem>(context), ITaskRepository
{
    public async Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status)
    {
        var cacheKey = $"Tasks_Status_{status}";
        if (_cache.TryGetValue(cacheKey, out IEnumerable<TaskItem>? cachedTasks))
            return cachedTasks ?? new List<TaskItem>();

        var tasks = await _context.Tasks
            .Where(t => t.Status == status)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .ToListAsync();

        _cache.Set(cacheKey, tasks, TimeSpan.FromMinutes(3));
        return tasks;
    }

    public async Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date)
    {
        var cacheKey = $"Tasks_Date_{date:yyyyMMdd}";
        if (_cache.TryGetValue(cacheKey, out IEnumerable<TaskItem>? cachedTasks))
            return cachedTasks ?? new List<TaskItem>();

        var tasks = await _context.Tasks
            .Where(t => t.PlannedDate.HasValue &&
                        t.PlannedDate.Value.Date == date.Date)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .ToListAsync();

        _cache.Set(cacheKey, tasks, TimeSpan.FromMinutes(5));
        return tasks;
    }

    public async Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority)
    {
        return await _context.Tasks
            .Where(t => t.UserPriority >= minPriority && t.UserPriority <= maxPriority)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetWithDependenciesAsync()
    {
        return await _context.Tasks
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetNotConfiguredAsync()
    {
        return await _context.Tasks
            .Where(t => t.Status == TaskStatus.NotConfigured)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetRecurringTasksAsync()
    {
        return await _context.Tasks
            .Where(t => t.IsRecurring && t.Recurrence != null)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task<IEnumerable<TaskItem>> GetChildTasksAsync(int parentTaskId)
    {
        return await _context.Tasks
            .Where(t => t.ParentTaskId == parentTaskId)
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(int taskId, TaskStatus status)
    {
        var task = await _context.Tasks.FindAsync(taskId);
        if (task != null)
        {
            task.Status = status;
            await _context.SaveChangesAsync();
            InvalidateCache();
        }
    }

    public async Task ProcessDayStartAsync()
    {
        var settings = await _context.UserSettings.FirstOrDefaultAsync();
        if (settings == null) return;

        var today = DateTime.Today;

        // Обработка гарантированных задач (приоритет 0)
        if (settings.AutoCompleteGuaranteed)
        {
            var guaranteedTasks = await _context.Tasks
                .Where(t => t.UserPriority == 0 &&
                            t.PlannedDate.HasValue &&
                            t.PlannedDate.Value.Date < today &&
                            t.Status != TaskStatus.Completed)
                .ToListAsync();

            foreach (var task in guaranteedTasks)
            {
                task.Status = TaskStatus.Completed;
            }
        }

        // Обработка неотложных задач (приоритет 1)
        if (settings.RemoveUrgentIfNotDone)
        {
            var urgentTasks = await _context.Tasks
                .Where(t => t.UserPriority == 1 &&
                            t.PlannedDate.HasValue &&
                            t.PlannedDate.Value.Date < today &&
                            t.Status != TaskStatus.Completed)
                .ToListAsync();

            foreach (var task in urgentTasks)
            {
                task.Status = TaskStatus.Irrelevant;
            }
        }

        // Перенос невыполненных задач на сегодня
        var unfinishedTasks = await _context.Tasks
            .Where(t => t.PlannedDate.HasValue &&
                        t.PlannedDate.Value.Date < today &&
                        t.Status == TaskStatus.Active)
            .ToListAsync();

        foreach (var task in unfinishedTasks)
        {
            task.PlannedDate = today;
        }

        await _context.SaveChangesAsync();
        InvalidateCache();
    }

    protected override void InvalidateCache()
    {
        base.InvalidateCache();
        // Дополнительная инвалидация кэша для задач
        var cacheKeys = new[] { "Tasks_Status_", "Tasks_Date_", "Tasks_" };
        foreach (var key in cacheKeys)
        {
            // В реальном приложении нужно более точное управление кэшем
        }
    }
}
