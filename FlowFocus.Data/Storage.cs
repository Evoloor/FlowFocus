using System.Text.Json;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
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
        });

        // Dependency configuration
        modelBuilder.Entity<Dependency>(entity =>
        {
            entity.ToTable(t =>
                t.HasCheckConstraint("CK_Dependency_SelfReference", "SourceTaskId != TargetTaskId"));
            entity.Property(d => d.Type).HasConversion<string>();
            entity.Property(d => d.Logic).HasConversion<string>();

            entity.HasOne(d => d.SourceTask)
                .WithMany(t => t.Dependencies)
                .HasForeignKey(d => d.SourceTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.TargetTask)
                .WithMany(t => t.DependentTasks)
                .HasForeignKey(d => d.TargetTaskId)
                .OnDelete(DeleteBehavior.Restrict);
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
            if (IsDirty || Cache is null)
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
    protected T? GetTrackedById(int id)
    {
        return GetTrackedQuery().FirstOrDefault(e => e.Id == id);
    }

    public virtual void Add(T entity)
    {
        lock (CacheLock)
        {
            if (entity.Id == 0)
            {
                entity.Id = GetNextId();
            }

            entity.LastChangesOn = DateTime.UtcNow;
            GetDbSet().Add(entity);
            context.SaveChanges();
            MarkDirty();
        }
    }

    private int GetNextId()
    {
        var maxId = context.Set<T>()
            .AsNoTracking()
            .Select(e => (int?)e.Id)
            .Max();
    
        if (maxId.HasValue)
            return maxId.Value + 1;
        return 1;
    }

    public virtual void Update(T entity)
    {
        lock (CacheLock)
        {
            try
            {
                Console.WriteLine($"Updating entity {typeof(T).Name} with ID {entity.Id}");

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
    protected void UpdatePartial(int id, Action<T> updateAction)
    {
        lock (CacheLock)
        {
            var entity = GetTrackedById(id);
            if (entity == null) return;
            updateAction(entity);
            entity.LastChangesOn = DateTime.UtcNow;
            context.SaveChanges();
            MarkDirty();
        }
    }

    public virtual void Delete(int id)
    {
        lock (CacheLock)
        {
            // Используем отслеживаемый запрос для удаления
            var entity = GetTrackedQuery().FirstOrDefault(e => e.Id == id);
            if (entity == null) return;
            GetDbSet().Remove(entity);
            context.SaveChanges();
            MarkDirty();
        }
    }

    public void SaveChanges()
    {
        context.SaveChanges();
        MarkDirty();
    }

    protected void MarkDirty() => IsDirty = true;

    protected void RefreshCache()
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
    private readonly StorageContext _context = context;
    protected override DbSet<TaskItem> GetDbSet() => _context.Tasks;

    protected override IQueryable<TaskItem> GetBaseQuery() =>
        _context.Tasks
            .AsNoTracking()
            .Include(t => t.Dependencies)
            .ThenInclude(d => d.TargetTask)
            .Include(t => t.DependentTasks)
            .ThenInclude(d => d.SourceTask)
            .OrderBy(t => t.PlannedDate);

    protected override IQueryable<TaskItem> GetTrackedQuery() =>
        _context.Tasks
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
        => GetAll().Where(t => t is { IsRecurring: true, Recurrence: not null }).ToList();

    public List<TaskItem> GetChildTasks(int parentTaskId)
        => GetAll().Where(t => t.ParentTaskId == parentTaskId).ToList();

    public void UpdateStatus(int taskId, TaskStatus status)
    {
        UpdatePartial(taskId, task => { task.Status = status; });
    }

    public void ProcessDayStart()
    {
        var settings = new SettingsRepository(_context).GetUserSettings();
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
            if (settings.AutoCompleteGuaranteed && task is { UserPriority: 0, Status: not TaskStatus.Completed })
            {
                task.Status = TaskStatus.Completed;
                modified = true;
            }
            // Обработка неотложных задач
            else if (settings.RemoveUrgentIfNotDone && task is { UserPriority: 1, Status: not TaskStatus.Completed })
            {
                task.Status = TaskStatus.Irrelevant;
                modified = true;
            }
            // Перенос активных задач
            else if (task.Status is TaskStatus.Active)
            {
                task.PlannedDate = today;
                modified = true;
            }

            if (task.Status != originalStatus)
            {
                task.LastChangesOn = DateTime.UtcNow;
            }
        }

        if (!modified) return;
        _context.SaveChanges();
        RefreshCache();
    }
}

public class DependencyRepository(StorageContext context) : CachedRepository<Dependency>(context), IDependencyRepository
{
    private readonly StorageContext _context = context;
    protected override DbSet<Dependency> GetDbSet() => _context.Dependencies;

    protected override IQueryable<Dependency> GetBaseQuery() =>
        _context.Dependencies
            .AsNoTracking()
            .Include(d => d.SourceTask)
            .Include(d => d.TargetTask);

    protected override IQueryable<Dependency> GetTrackedQuery() =>
        _context.Dependencies
            .Include(d => d.SourceTask)
            .Include(d => d.TargetTask);

    public List<Dependency> GetDependenciesForTask(int taskId)
        => GetAll().Where(d => d.SourceTaskId == taskId).ToList();

    public List<Dependency> GetDependentTasks(int taskId)
        => GetAll().Where(d => d.TargetTaskId == taskId).ToList();

    public bool HasCircularDependency(int sourceTaskId, int targetTaskId)
    {
        var hasDirectCircular = GetBaseQuery()
            .Any(d => d.SourceTaskId == targetTaskId && d.TargetTaskId == sourceTaskId);

        return hasDirectCircular || CheckTransitiveDependency(targetTaskId, sourceTaskId, []);
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

        _context.SaveChanges();
        MarkDirty();
    }

    public List<Dependency> GetBlockingDependencies(int taskId)
        => GetAll().Where(d => d.SourceTaskId == taskId && d.Type == DependencyType.Blocking).ToList();

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
    private readonly StorageContext _context = context;
    protected override DbSet<UserSettings> GetDbSet() => _context.UserSettings;

    protected override IQueryable<UserSettings> GetTrackedQuery() =>
        _context.UserSettings.AsQueryable();

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
                    _context.SaveChanges();
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
        if (settings == null) return;
        // Сброс к значениям по умолчанию вместо удаления
        var defaultSettings = new UserSettings { Id = settings.Id };
        _context.Entry(settings).CurrentValues.SetValues(defaultSettings);
        settings.LastChangesOn = DateTime.UtcNow;
        _context.SaveChanges();
        MarkDirty();
    }
}
