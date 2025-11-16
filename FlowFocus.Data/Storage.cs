using System.Text.Json;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    public StorageContext()
    {
    }

    public StorageContext(DbContextOptions<StorageContext> options) : base(options)
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
                        : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<RecurrencePattern>(v, (JsonSerializerOptions?)null)
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

public abstract class CachedRepository<T>(StorageContext context) : IRepository<T>
    where T : class, IAuditEntity
{
    protected List<T>? Cache;
    protected bool IsDirty = true;
    protected readonly object CacheLock = new();

    protected abstract DbSet<T> GetDbSet();

    // Для операций чтения - без отслеживания
    protected virtual IQueryable<T> GetBaseQuery() =>
        GetDbSet().AsNoTracking().AsQueryable();

    // Для операций записи - с отслеживанием
    protected virtual IQueryable<T> GetTrackedQuery() =>
        GetDbSet().AsQueryable();

    public virtual List<T> GetAll()
    {
        lock (CacheLock)
        {
            if (IsDirty || Cache == null)
            {
                Cache = GetBaseQuery().ToList();
                IsDirty = false;
            }

            return Cache.ToList(); // Возвращаем копию
        }
    }

    public virtual T? GetById(int id)
    {
        // Для операций чтения используем кэш (без отслеживания)
        return GetAll().FirstOrDefault(e => e.Id == id);
    }

    // Метод для получения отслеживаемой сущности (только для операций изменения)
    protected virtual T? GetTrackedById(int id)
    {
        return GetTrackedQuery().FirstOrDefault(e => e.Id == id);
    }

    public virtual void Add(T entity)
    {
        lock (CacheLock)
        {
            if (entity.Id == 0)
            {
                var maxId = GetBaseQuery()
                    .Select(e => e.Id)
                    .DefaultIfEmpty(0)
                    .Max();
                entity.Id = maxId + 1;
            }

            entity.LastChangesOn = DateTime.UtcNow;

            // Добавляем новую сущность
            GetDbSet().Add(entity);
            context.SaveChanges();
            MarkDirty();
        }
    }

    public virtual void Update(T entity)
    {
        lock (CacheLock)
        {
            try
            {
                Console.WriteLine($"Updating entity {typeof(T).Name} with ID {entity.Id}");

                // Получаем отслеживаемую сущность из БД
                var trackedEntity = GetTrackedById(entity.Id);
                if (trackedEntity == null)
                {
                    throw new InvalidOperationException($"Entity with ID {entity.Id} not found");
                }

                // Обновляем свойства отслеживаемой сущности
                context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                trackedEntity.LastChangesOn = DateTime.UtcNow;

                var result = context.SaveChanges();
                Console.WriteLine($"SaveChanges affected {result} records");

                MarkDirty();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Update: {ex.Message}");
                throw;
            }
        }
    }

    // Альтернативный метод Update с явным указанием изменяемых свойств
    public virtual void UpdatePartial(int id, Action<T> updateAction)
    {
        lock (CacheLock)
        {
            var entity = GetTrackedById(id);
            if (entity != null)
            {
                updateAction(entity);
                entity.LastChangesOn = DateTime.UtcNow;
                context.SaveChanges();
                MarkDirty();
            }
        }
    }

    public virtual void Delete(int id)
    {
        lock (CacheLock)
        {
            // Используем отслеживаемый запрос для удаления
            var entity = GetTrackedQuery().FirstOrDefault(e => e.Id == id);
            if (entity != null)
            {
                GetDbSet().Remove(entity);
                context.SaveChanges();
                MarkDirty();
            }
        }
    }

    public void SaveChanges()
    {
        context.SaveChanges();
        MarkDirty();
    }

    protected void MarkDirty() => IsDirty = true;

    public virtual void RefreshCache()
    {
        lock (CacheLock)
        {
            IsDirty = true;
            Cache = null;
        }
    }
}

public class TaskRepository(StorageContext context) : CachedRepository<TaskItem>(context), ITaskRepository
{
    protected override DbSet<TaskItem> GetDbSet() => context.Tasks;

    protected override IQueryable<TaskItem> GetBaseQuery() =>
        context.Tasks
            .AsNoTracking()
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .OrderBy(t => t.PlannedDate);

    protected override IQueryable<TaskItem> GetTrackedQuery() =>
        context.Tasks
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .OrderBy(t => t.PlannedDate);

    public List<TaskItem> GetByStatus(TaskStatus status)
        => GetAll().Where(t => t.Status == status).ToList();

    public List<TaskItem> GetByDate(DateTime date)
        => GetAll().Where(t => t.PlannedDate.HasValue && t.PlannedDate.Value.Date == date.Date).ToList();

    public List<TaskItem> GetByPriorityRange(int minPriority, int maxPriority)
        => GetAll().Where(t => t.UserPriority >= minPriority && t.UserPriority <= maxPriority).ToList();

    public List<TaskItem> GetWithDependencies()
        => GetAll();

    public List<TaskItem> GetNotConfigured()
        => GetByStatus(TaskStatus.NotConfigured);

    public List<TaskItem> GetRecurringTasks()
        => GetAll().Where(t => t.IsRecurring && t.Recurrence != null).ToList();

    public List<TaskItem> GetChildTasks(int parentTaskId)
        => GetAll().Where(t => t.ParentTaskId == parentTaskId).ToList();

    public void UpdateStatus(int taskId, TaskStatus status)
    {
        UpdatePartial(taskId, task => { task.Status = status; });
    }

    public void ProcessDayStart()
    {
        var settings = new SettingsRepository(context).GetUserSettings();
        var today = DateTime.Today;
        var modified = false;

        // Используем отслеживаемые сущности для изменений
        var tasksToProcess = GetTrackedQuery()
            .Where(t => t.PlannedDate.HasValue && t.PlannedDate.Value.Date < today)
            .ToList();

        foreach (var task in tasksToProcess)
        {
            var originalStatus = task.Status;

            // Обработка гарантированных задач
            if (settings.AutoCompleteGuaranteed && task.UserPriority == 0 && task.Status != TaskStatus.Completed)
            {
                task.Status = TaskStatus.Completed;
                modified = true;
            }
            // Обработка неотложных задач
            else if (settings.RemoveUrgentIfNotDone && task.UserPriority == 1 && task.Status != TaskStatus.Completed)
            {
                task.Status = TaskStatus.Irrelevant;
                modified = true;
            }
            // Перенос активных задач
            else if (task.Status == TaskStatus.Active)
            {
                task.PlannedDate = today;
                modified = true;
            }

            if (task.Status != originalStatus || task.PlannedDate != task.PlannedDate)
            {
                task.LastChangesOn = DateTime.UtcNow;
            }
        }

        if (modified)
        {
            context.SaveChanges();
            RefreshCache();
        }
    }
}

public class DependencyRepository(StorageContext context) : CachedRepository<Dependency>(context), IDependencyRepository
{
    protected override DbSet<Dependency> GetDbSet() => context.Dependencies;

    protected override IQueryable<Dependency> GetBaseQuery() =>
        context.Dependencies
            .AsNoTracking()
            .Include(d => d.SourceTask)
            .Include(d => d.TargetTask);

    protected override IQueryable<Dependency> GetTrackedQuery() =>
        context.Dependencies
            .Include(d => d.SourceTask)
            .Include(d => d.TargetTask);

    public List<Dependency> GetDependenciesForTask(int taskId)
        => GetAll().Where(d => d.SourceTaskId == taskId).ToList();

    public List<Dependency> GetDependentTasks(int taskId)
        => GetAll().Where(d => d.TargetTaskId == taskId).ToList();

    public bool HasCircularDependency(int sourceTaskId, int targetTaskId)
    {
        // Для проверки зависимостей используем неотслеживаемый запрос
        var hasDirectCircular = GetBaseQuery()
            .Any(d => d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId);

        if (hasDirectCircular)
            return true;

        return CheckTransitiveDependency(targetTaskId, sourceTaskId, new HashSet<int>());
    }

    private bool CheckTransitiveDependency(int currentTaskId, int targetTaskId, HashSet<int> visited)
    {
        if (!visited.Add(currentTaskId))
            return false;

        var dependencies = GetBaseQuery()
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

    public void RemoveDependenciesForTask(int taskId)
    {
        // Для удаления используем отслеживаемые сущности
        var dependencies = GetTrackedQuery()
            .Where(d => d.SourceTaskId == taskId || d.TargetTaskId == taskId)
            .ToList();

        foreach (var dependency in dependencies)
        {
            GetDbSet().Remove(dependency);
        }

        context.SaveChanges();
        MarkDirty();
    }

    public List<Dependency> GetBlockingDependencies(int taskId)
        => GetAll().Where(d => d.SourceTaskId == taskId && d.Type == DependencyType.Blocking).ToList();

    // Новые методы для работы с зависимостями
    public void AddDependency(int sourceTaskId, int targetTaskId, DependencyType type,
        DependencyLogic logic = DependencyLogic.And)
    {
        var dependency = new Dependency
        {
            SourceTaskId = sourceTaskId,
            TargetTaskId = targetTaskId,
            Type = type,
            Logic = logic,
            LastChangesOn = DateTime.UtcNow
        };

        Add(dependency);
    }

    public void RemoveDependency(int sourceTaskId, int targetTaskId)
    {
        var dependency = GetTrackedQuery()
            .FirstOrDefault(d => d.SourceTaskId == sourceTaskId && d.TargetTaskId == targetTaskId);

        if (dependency != null)
        {
            Delete(dependency.Id);
        }
    }
}

public class SettingsRepository(StorageContext context) : CachedRepository<UserSettings>(context), ISettingsRepository
{
    protected override DbSet<UserSettings> GetDbSet() => context.UserSettings;

    protected override IQueryable<UserSettings> GetTrackedQuery() =>
        context.UserSettings.AsQueryable();

    public override List<UserSettings> GetAll()
    {
        lock (CacheLock)
        {
            if (IsDirty || Cache == null)
            {
                var settings = GetTrackedQuery().FirstOrDefault();
                if (settings == null)
                {
                    settings = new UserSettings();
                    GetDbSet().Add(settings);
                    context.SaveChanges();
                }

                Cache = [settings];
                IsDirty = false;
            }

            return Cache.ToList();
        }
    }

    public override UserSettings? GetById(int id) => GetAll().FirstOrDefault();

    public UserSettings GetUserSettings() => GetAll().First();

    public bool GetAutoRecalculateSetting() => GetUserSettings().AutoRecalculateOnAdd;

    public void UpdateDayStartHour(int hour)
    {
        UpdatePartial(1, settings => { settings.DayStartHour = Math.Clamp(hour, 0, 23); });
    }

    public void UpdateAutoRecalculate(bool autoRecalculate)
    {
        UpdatePartial(1, settings => { settings.AutoRecalculateOnAdd = autoRecalculate; });
    }

    public override void Add(UserSettings entity)
    {
        // Для настроек используем Update вместо Add
        Update(entity);
    }

    public override void Delete(int id)
    {
        var settings = GetTrackedById(id);
        if (settings != null)
        {
            // Сброс к значениям по умолчанию вместо удаления
            var defaultSettings = new UserSettings { Id = settings.Id };
            context.Entry(settings).CurrentValues.SetValues(defaultSettings);
            settings.LastChangesOn = DateTime.UtcNow;
            context.SaveChanges();
            MarkDirty();
        }
    }
}