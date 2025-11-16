using System.Text.Json;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data;

public abstract class CachedRepository<T>(StorageContext context) : IRepository<T>
    where T : AuditEntity
{
    protected List<T>? Cache;
    protected bool IsDirty = true;
    protected readonly object CacheLock = new();

    protected abstract DbSet<T> GetDbSet();
    protected virtual IQueryable<T> GetBaseQuery() => GetDbSet().AsQueryable();

    public virtual List<T> GetAll()
    {
        lock (CacheLock)
        {
            if (IsDirty || Cache == null)
            {
                Cache = GetBaseQuery().ToList();
                IsDirty = false;
            }

            return Cache.ToList(); // Возвращаем копию для безопасности
        }
    }

    public virtual T? GetById(int id)
    {
        return GetAll().FirstOrDefault(e => e.Id == id);
    }

    public virtual void Add(T entity)
    {
        lock (CacheLock)
        {
            if (entity.Id == 0)
            {
                var allEntities = GetAll();
                entity.Id = allEntities.Count > 0 ? allEntities.Max(e => e.Id) + 1 : 1;
            }

            entity.LastChange = DateTime.Now;
            GetDbSet().Add(entity);
            MarkDirty();
        }
    }

    public virtual void Update(T entity)
    {
        lock (CacheLock)
        {
            entity.LastChange = DateTime.Now;
            GetDbSet().Update(entity);
            MarkDirty();
        }
    }

    public virtual void Delete(int id)
    {
        lock (CacheLock)
        {
            var entity = GetById(id);
            if (entity == null) return;
            GetDbSet().Remove(entity);
            MarkDirty();
        }
    }

    public virtual void SaveChanges()
    {
        lock (CacheLock)
        {
            context.SaveChanges();
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    // Метод для принудительного обновления кеша
    public virtual void RefreshCache()
    {
        lock (CacheLock)
        {
            IsDirty = true;
            Cache = null;
        }
    }
}

// Обновлённые интерфейсы с наследованием от EntityBase
public interface IRepository<T> where T : AuditEntity
{
    List<T> GetAll();
    T? GetById(int id);
    void Add(T entity);
    void Update(T entity);
    void Delete(int id);
    void SaveChanges();
}

public interface ITaskRepository : IRepository<TaskItem>
{
    List<TaskItem> GetByStatus(TodoTaskStatus status);
    List<TaskItem> GetByDate(DateTime date, TimeSpan? dayStartTime = null);
    List<TaskItem> GetUnconfigured();
}

// Обновлённый TaskRepository с наследованием от CachedRepository
public class TaskRepository(StorageContext context) : CachedRepository<TaskItem>(context), ITaskRepository
{
    private readonly StorageContext _context = context;
    protected override DbSet<TaskItem> GetDbSet() => _context.Tasks;

    protected override IQueryable<TaskItem> GetBaseQuery() =>
        _context.Tasks.Include(t => t.Blockers).OrderBy(t => t.Deadline);

    // Специфичные методы
    public List<TaskItem> GetByStatus(TodoTaskStatus status)
        => GetAll().Where(t => t.Status == status).ToList();

    public List<TaskItem> GetByDate(DateTime date, TimeSpan? dayStartTime = null)
    {
        var dateStart = date.StartOfToday(dayStartTime);
        var nextDayStart = dateStart.AddDays(1);

        return GetAll().Where(t =>
            t.Deadline.HasValue && IsTaskInDateRange(t.Deadline.Value, date, dateStart, nextDayStart)
        ).ToList();

        static bool IsTaskInDateRange(DateTime taskDate, DateTime targetDate, DateTime dateStart, DateTime nextDayStart)
        {
            // Задачи на целый день (время = 00:00:00)
            if (taskDate.TimeOfDay == TimeSpan.Zero)
            {
                return taskDate.Date == targetDate.Date;
            }

            // Задачи с конкретным временем
            return taskDate >= dateStart && taskDate < nextDayStart;
        }
    }


    public List<TaskItem> GetUnconfigured()
        => GetByStatus(TodoTaskStatus.Unconfigured);
}

// Обновлённый SettingsRepository с наследованием от CachedRepository
public class SettingsRepository(StorageContext context) : CachedRepository<UserAppSettings>(context)
{
    private readonly StorageContext _context = context;
    protected override DbSet<UserAppSettings> GetDbSet() => _context.Settings;

    public override List<UserAppSettings> GetAll()
    {
        lock (CacheLock)
        {
            if (IsDirty || Cache == null)
            {
                var settings = _context.Settings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new UserAppSettings();
                    _context.Settings.Add(settings);
                    _context.SaveChanges();
                }

                Cache = [settings];
                IsDirty = false;
            }

            return Cache.ToList();
        }
    }

    public override UserAppSettings? GetById(int id) => GetAll().FirstOrDefault();

    // Для настроек переопределяем методы, т.к. у нас всегда одна запись
    public override void Add(UserAppSettings entity)
    {
        // Для настроек используем Update вместо Add
        Update(entity);
    }

    public override void Delete(int id)
    {
        // Для настроек не разрешаем удаление, только сброс
        var settings = GetById(id);
        if (settings != null)
        {
            settings.ResetToDefaults();
            Update(settings);
        }
    }
}

// Дополнительный репозиторий для TaskBlocker
public class TaskBlockerRepository(StorageContext context) : CachedRepository<TaskBlocker>(context)
{
    private readonly StorageContext _context = context;
    protected override DbSet<TaskBlocker> GetDbSet() => _context.TaskBlockers;

    // Специфичные методы для блокеров
    public List<TaskBlocker> GetByParentTaskId(int parentTaskId)
        => GetAll().Where(b => b.ParentTaskId == parentTaskId).ToList();

    public List<TaskBlocker> GetByBlockerTaskId(int blockerTaskId)
        => GetAll().Where(b => b.BlockerTaskId == blockerTaskId).ToList();
}

// Обновлённый StorageContext (без изменений, оставляем для полноты)
public class StorageContext : DbContext
{
    private readonly string _dbPath;

    public StorageContext(string? dbPath = null)
    {
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(folder))
            folder = ".";
        //_dbPath = Path.Combine(folder, dbPath ?? "flowfocus.db");
        _dbPath = "flowfocus.db";
    }

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

            entity.Property(e => e.Tags)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );

            entity.Property(e => e.Repeat)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<RepeatInfo>(v, (JsonSerializerOptions?)null)
                );

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Deadline);
            entity.HasIndex(e => e.IsFavorite);
        });

        // TaskBlocker
        modelBuilder.Entity<TaskBlocker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ParentTaskId);
            entity.HasIndex(e => e.BlockerTaskId);
        });

        // UserAppSettings
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