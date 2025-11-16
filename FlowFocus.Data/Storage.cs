using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

public class DependencyRepository(AppDbContext context, DataCache dataCache) 
    : BaseRepository<Dependency>(context, dataCache), IDependencyRepository
{
    public override Task<Dependency?> GetByIdAsync(int id)
    {
        var dependency = _dataCache.GetAllDependencies().FirstOrDefault(d => d.Id == id);
        return Task.FromResult(dependency);
    }

    public override Task<IEnumerable<Dependency>> GetAllAsync()
    {
        var dependencies = _dataCache.GetAllDependencies();
        return Task.FromResult(dependencies.AsEnumerable());
    }

    public override async Task AddAsync(Dependency entity)
    {
        _dataCache.AddDependency(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task UpdateAsync(Dependency entity)
    {
        // Для зависимостей проще удалить и добавить заново
        _dataCache.RemoveDependency(entity.Id);
        _dataCache.AddDependency(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task DeleteAsync(int id)
    {
        _dataCache.RemoveDependency(id);
        await SyncToDatabaseAsync();
    }

    public override Task<bool> ExistsAsync(int id)
    {
        var exists = _dataCache.GetAllDependencies().Any(d => d.Id == id);
        return Task.FromResult(exists);
    }

    public Task<IEnumerable<Dependency>> GetDependenciesForTaskAsync(int taskId)
    {
        var dependencies = _dataCache.GetAllDependencies()
            .Where(d => d.SourceTaskId == taskId)
            .ToList();
        return Task.FromResult(dependencies.AsEnumerable());
    }

    public Task<IEnumerable<Dependency>> GetDependentTasksAsync(int taskId)
    {
        var dependencies = _dataCache.GetAllDependencies()
            .Where(d => d.TargetTaskId == taskId)
            .ToList();
        return Task.FromResult(dependencies.AsEnumerable());
    }

    public Task<bool> HasCircularDependencyAsync(int sourceTaskId, int targetTaskId)
    {
        // Проверка прямой циклической зависимости
        var hasDirectCircular = _dataCache.GetAllDependencies()
            .Any(d => d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId);
            
        if (hasDirectCircular)
            return Task.FromResult(true);

        // Проверка транзитивных зависимостей через рекурсивный запрос
        return Task.FromResult(CheckTransitiveDependency(targetTaskId, sourceTaskId, []));
    }

    private bool CheckTransitiveDependency(int currentTaskId, int targetTaskId, HashSet<int> visited)
    {
        if (!visited.Add(currentTaskId))
            return false;

        var dependencies = _dataCache.GetAllDependencies()
            .Where(d => d.SourceTaskId == currentTaskId)
            .Select(d => d.TargetTaskId)
            .ToList();

        foreach (var dependencyId in dependencies)
        {
            if (dependencyId == targetTaskId)
                return true;

            if (CheckTransitiveDependency(dependencyId, targetTaskId, visited))
                return true;
        }

        visited.Remove(currentTaskId);
        return false;
    }

    public async Task RemoveDependenciesForTaskAsync(int taskId)
    {
        _dataCache.RemoveDependenciesForTask(taskId);
        await SyncToDatabaseAsync();
    }

    public Task<IEnumerable<Dependency>> GetBlockingDependenciesAsync(int taskId)
    {
        var dependencies = _dataCache.GetAllDependencies()
            .Where(d => d.SourceTaskId == taskId && d.Type == DependencyType.Blocking)
            .ToList();
        return Task.FromResult(dependencies.AsEnumerable());
    }
}

public class SettingsRepository(AppDbContext context, DataCache dataCache) 
    : BaseRepository<UserSettings>(context, dataCache), ISettingsRepository
{
    public override Task<UserSettings?> GetByIdAsync(int id)
    {
        var settings = _dataCache.GetUserSettings();
        return Task.FromResult<UserSettings?>(settings);
    }

    public override Task<IEnumerable<UserSettings>> GetAllAsync()
    {
        var settings = new List<UserSettings> { _dataCache.GetUserSettings() };
        return Task.FromResult(settings.AsEnumerable());
    }

    public override async Task AddAsync(UserSettings entity)
    {
        _dataCache.SetUserSettings(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task UpdateAsync(UserSettings entity)
    {
        _dataCache.SetUserSettings(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task DeleteAsync(int id)
    {
        // Создаем настройки по умолчанию
        _dataCache.SetUserSettings(new UserSettings());
        await SyncToDatabaseAsync();
    }

    public override Task<bool> ExistsAsync(int id)
    {
        return Task.FromResult(true); // Настройки всегда существуют
    }

    public Task<UserSettings> GetUserSettingsAsync()
    {
        var settings = _dataCache.GetUserSettings();
        return Task.FromResult(settings);
    }

    public Task<bool> GetAutoRecalculateSettingAsync()
    {
        var settings = _dataCache.GetUserSettings();
        return Task.FromResult(settings.AutoRecalculateOnAdd);
    }

    public async Task UpdateDayStartHourAsync(int hour)
    {
        var settings = _dataCache.GetUserSettings();
        var updatedSettings = settings.Clone();
        updatedSettings.DayStartHour = Math.Clamp(hour, 0, 23);
        _dataCache.SetUserSettings(updatedSettings);
        await SyncToDatabaseAsync();
    }

    // Метод для загрузки настроек при старте приложения
    public async Task LoadSettingsAsync()
    {
        var settings = await _context.UserSettings
            .AsNoTracking()
            .FirstOrDefaultAsync();
            
        if (settings != null)
        {
            _dataCache.SetUserSettings(settings);
        }
    }
}

public static class ServiceExtensions
{
    public static IServiceCollection AddDataLayer(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>();
        services.AddSingleton<DataCache>(); // Единый кэш на все приложение

        // Репозитории
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<IDependencyRepository, DependencyRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        // Сервисы
        services.AddScoped<IRecurringTaskService, RecurringTaskService>();
        services.AddScoped<IPlannerService, BasicPlannerService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IProcrastinationService, ProcrastinationService>();

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Автоматическое создание БД и применение миграций
        await context.Database.MigrateAsync();

        // Загружаем все данные в кэш
        var taskRepo = scope.ServiceProvider.GetRequiredService<ITaskRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        if (taskRepo is TaskRepository concreteTaskRepo)
        {
            await concreteTaskRepo.LoadAllDataAsync();
        }

        if (settingsRepo is SettingsRepository concreteSettingsRepo)
        {
            await concreteSettingsRepo.LoadSettingsAsync();
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
                Tags = [..task.Tags],
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
        modelBuilder.Entity<UserSettings>(entity => { entity.HasData(new UserSettings { Id = 1 }); });
    }
}

public class TaskRepository(AppDbContext context, DataCache dataCache) 
    : BaseRepository<TaskItem>(context, dataCache), ITaskRepository
{
    public override Task<TaskItem?> GetByIdAsync(int id)
    {
        var task = _dataCache.GetAllTasks().FirstOrDefault(t => t.Id == id);
        return Task.FromResult(task);
    }

    public override Task<IEnumerable<TaskItem>> GetAllAsync()
    {
        var tasks = _dataCache.GetAllTasks();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public override async Task AddAsync(TaskItem entity)
    {
        _dataCache.AddTask(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task UpdateAsync(TaskItem entity)
    {
        _dataCache.UpdateTask(entity);
        await SyncToDatabaseAsync();
    }

    public override async Task DeleteAsync(int id)
    {
        _dataCache.RemoveTask(id);
        _dataCache.RemoveDependenciesForTask(id);
        await SyncToDatabaseAsync();
    }

    public override Task<bool> ExistsAsync(int id)
    {
        var exists = _dataCache.GetAllTasks().Any(t => t.Id == id);
        return Task.FromResult(exists);
    }

    public Task<IEnumerable<TaskItem>> GetByStatusAsync(TaskStatus status)
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.Status == status)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetByDateAsync(DateTime date)
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.PlannedDate.HasValue &&
                        t.PlannedDate.Value.Date == date.Date)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetByPriorityRangeAsync(int minPriority, int maxPriority)
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.UserPriority >= minPriority && t.UserPriority <= maxPriority)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetWithDependenciesAsync()
    {
        var tasks = _dataCache.GetAllTasks();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetNotConfiguredAsync()
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.Status == TaskStatus.NotConfigured)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetRecurringTasksAsync()
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.IsRecurring && t.Recurrence != null)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public Task<IEnumerable<TaskItem>> GetChildTasksAsync(int parentTaskId)
    {
        var tasks = _dataCache.GetAllTasks()
            .Where(t => t.ParentTaskId == parentTaskId)
            .ToList();
        return Task.FromResult(tasks.AsEnumerable());
    }

    public async Task UpdateStatusAsync(int taskId, TaskStatus status)
    {
        var task = _dataCache.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            var updatedTask = task.Clone();
            updatedTask.Status = status;
            _dataCache.UpdateTask(updatedTask);
            await SyncToDatabaseAsync();
        }
    }

    public async Task ProcessDayStartAsync()
    {
        var settings = _dataCache.GetUserSettings();
        var today = DateTime.Today;
        var allTasks = _dataCache.GetAllTasks();
        var modified = false;

        // Обработка гарантированных задач (приоритет 0)
        if (settings.AutoCompleteGuaranteed)
        {
            var guaranteedTasks = allTasks
                .Where(t => t.UserPriority == 0 &&
                            t.PlannedDate.HasValue &&
                            t.PlannedDate.Value.Date < today &&
                            t.Status != TaskStatus.Completed)
                .ToList();

            foreach (var task in guaranteedTasks)
            {
                var updatedTask = task.Clone();
                updatedTask.Status = TaskStatus.Completed;
                _dataCache.UpdateTask(updatedTask);
                modified = true;
            }
        }

        // Обработка неотложных задач (приоритет 1)
        if (settings.RemoveUrgentIfNotDone)
        {
            var urgentTasks = allTasks
                .Where(t => t.UserPriority == 1 &&
                            t.PlannedDate.HasValue &&
                            t.PlannedDate.Value.Date < today &&
                            t.Status != TaskStatus.Completed)
                .ToList();

            foreach (var task in urgentTasks)
            {
                var updatedTask = task.Clone();
                updatedTask.Status = TaskStatus.Irrelevant;
                _dataCache.UpdateTask(updatedTask);
                modified = true;
            }
        }

        // Перенос невыполненных задач на сегодня
        var unfinishedTasks = allTasks
            .Where(t => t.PlannedDate.HasValue &&
                        t.PlannedDate.Value.Date < today &&
                        t.Status == TaskStatus.Active)
            .ToList();

        foreach (var task in unfinishedTasks)
        {
            var updatedTask = task.Clone();
            updatedTask.PlannedDate = today;
            _dataCache.UpdateTask(updatedTask);
            modified = true;
        }

        if (modified)
        {
            await SyncToDatabaseAsync();
        }
    }

    // Метод для загрузки всех данных при старте приложения
    public async Task LoadAllDataAsync()
    {
        var tasks = await _context.Tasks
            .AsNoTracking()
            .ToListAsync();
            
        var dependencies = await _context.Dependencies
            .AsNoTracking()
            .ToListAsync();

        _dataCache.SetAllTasks(tasks);
        _dataCache.SetAllDependencies(dependencies);
    }
}

public class DataCache
{
    private readonly object _lock = new object();
    private List<TaskItem> _allTasks = new();
    private List<Dependency> _allDependencies = new();
    private UserSettings _userSettings = new();

    public List<TaskItem> GetAllTasks()
    {
        lock (_lock)
        {
            return new List<TaskItem>(_allTasks);
        }
    }

    public void SetAllTasks(List<TaskItem> tasks)
    {
        lock (_lock)
        {
            _allTasks = new List<TaskItem>(tasks);
        }
    }

    public List<Dependency> GetAllDependencies()
    {
        lock (_lock)
        {
            return new List<Dependency>(_allDependencies);
        }
    }

    public void SetAllDependencies(List<Dependency> dependencies)
    {
        lock (_lock)
        {
            _allDependencies = new List<Dependency>(dependencies);
        }
    }

    public UserSettings GetUserSettings()
    {
        lock (_lock)
        {
            return _userSettings.Clone();
        }
    }

    public void SetUserSettings(UserSettings settings)
    {
        lock (_lock)
        {
            _userSettings = settings.Clone();
        }
    }

    // Методы для атомарных операций с задачами
    public void AddTask(TaskItem task)
    {
        lock (_lock)
        {
            _allTasks.Add(task);
        }
    }

    public void UpdateTask(TaskItem updatedTask)
    {
        lock (_lock)
        {
            var existingTask = _allTasks.FirstOrDefault(t => t.Id == updatedTask.Id);
            if (existingTask != null)
            {
                _allTasks.Remove(existingTask);
                _allTasks.Add(updatedTask);
            }
        }
    }

    public void RemoveTask(int taskId)
    {
        lock (_lock)
        {
            _allTasks.RemoveAll(t => t.Id == taskId);
        }
    }

    // Методы для зависимостей
    public void AddDependency(Dependency dependency)
    {
        lock (_lock)
        {
            _allDependencies.Add(dependency);
        }
    }

    public void RemoveDependency(int dependencyId)
    {
        lock (_lock)
        {
            _allDependencies.RemoveAll(d => d.Id == dependencyId);
        }
    }

    public void RemoveDependenciesForTask(int taskId)
    {
        lock (_lock)
        {
            _allDependencies.RemoveAll(d => 
                d.SourceTaskId == taskId || d.TargetTaskId == taskId);
        }
    }
}

public abstract class BaseRepository<T>(AppDbContext context, DataCache dataCache) : IRepository<T> where T : class
{
    protected readonly AppDbContext _context = context;
    protected readonly DataCache _dataCache = dataCache;

    public abstract Task<T?> GetByIdAsync(int id);
    public abstract Task<IEnumerable<T>> GetAllAsync();
    public abstract Task AddAsync(T entity);
    public abstract Task UpdateAsync(T entity);
    public abstract Task DeleteAsync(int id);
    public abstract Task<bool> ExistsAsync(int id);

    protected virtual async Task SyncToDatabaseAsync()
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Логируем ошибку, но не прерываем работу приложения
            Console.WriteLine($"Ошибка синхронизации с БД: {ex.Message}");
        }
    }
}
public static class ModelExtensions
{
    public static TaskItem Clone(this TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            UserPriority = task.UserPriority,
            Status = task.Status,
            Interest = task.Interest,
            Complexity = task.Complexity,
            EstimatedHours = task.EstimatedHours,
            PlannedDate = task.PlannedDate,
            IsRecurring = task.IsRecurring,
            Recurrence = task.Recurrence,
            RecurrenceEndDate = task.RecurrenceEndDate,
            ParentTaskId = task.ParentTaskId,
            Tags = new List<string>(task.Tags),
            DisplayType = task.DisplayType,
            CreatedDate = task.CreatedDate
        };
    }

    public static UserSettings Clone(this UserSettings settings)
    {
        return new UserSettings
        {
            Id = settings.Id,
            DayStartHour = settings.DayStartHour,
            AutoRecalculateOnAdd = settings.AutoRecalculateOnAdd,
            AutoCompleteGuaranteed = settings.AutoCompleteGuaranteed,
            RemoveUrgentIfNotDone = settings.RemoveUrgentIfNotDone
        };
    }
}
