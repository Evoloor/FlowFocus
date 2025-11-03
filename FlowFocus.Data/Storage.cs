using System.Text.Json;
using FlowFocus.Core.Models;
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

public interface IRepository<T> where T : class
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
    List<TaskItem> GetByDate(DateTime date);
    List<TaskItem> GetUnconfigured();
    List<TaskItem> GetToday();
}

public class TaskRepository : ITaskRepository
{
    private readonly StorageContext _context;
    private List<TaskItem>? _cache;
    private bool _isDirty = true;

    public TaskRepository(StorageContext context)
    {
        _context = context;
    }

    public List<TaskItem> GetAll()
    {
        if (_isDirty || _cache == null)
        {
            _cache = _context.Tasks
                .Include(t => t.Blockers)
                .OrderBy(t => t.AssignedDate)
                .ToList();
            _isDirty = false;
        }

        return _cache;
    }

    public TaskItem? GetById(int id) => GetAll().FirstOrDefault(t => t.Id == id);

    public void Add(TaskItem task)
    {
        if (task.Id == 0)
        {
            task.Id = GetAll().Count > 0 ? GetAll().Max(t => t.Id) + 1 : 1;
        }

        task.LastChange = DateTime.Now;

        _context.Tasks.Add(task);
        _isDirty = true;
    }

    public void Update(TaskItem task)
    {
        task.LastChange = DateTime.Now;
        _context.Tasks.Update(task);
        _isDirty = true;
    }

    public void Delete(int id)
    {
        var task = GetById(id);
        if (task != null)
        {
            _context.Tasks.Remove(task);
            _isDirty = true;
        }
    }

    public void SaveChanges()
    {
        _context.SaveChanges();
        _isDirty = true;
    }

    // Специфичные методы
    public List<TaskItem> GetByStatus(TodoTaskStatus status)
        => GetAll().Where(t => t.Status == status).ToList();

    public List<TaskItem> GetByDate(DateTime date)
        => GetAll().Where(t => t.AssignedDate?.Date == date.Date).ToList();

    public List<TaskItem> GetUnconfigured()
        => GetByStatus(TodoTaskStatus.Unconfigured);

    public List<TaskItem> GetToday()
        => GetByDate(DateTime.Today);
}

public class SettingsRepository : IRepository<UserAppSettings>
{
    private readonly StorageContext _context;
    private UserAppSettings? _cache;
    private bool _isDirty = true;

    public SettingsRepository(StorageContext context)
    {
        _context = context;
    }

    public List<UserAppSettings> GetAll()
    {
        if (_isDirty || _cache == null)
        {
            _cache = _context.Settings.FirstOrDefault();
            if (_cache == null)
            {
                _cache = new UserAppSettings();
                _context.Settings.Add(_cache);
                _context.SaveChanges();
            }

            _isDirty = false;
        }

        return new List<UserAppSettings> { _cache };
    }

    public UserAppSettings? GetById(int id) => GetAll().FirstOrDefault();

    public void Add(UserAppSettings entity)
    {
        // Настройки всегда одна запись
        _context.Settings.Add(entity);
        _isDirty = true;
    }

    public void Update(UserAppSettings entity)
    {
        _context.Settings.Update(entity);
        _isDirty = true;
    }

    public void Delete(int id)
    {
        var settings = GetById(id);
        if (settings != null)
        {
            _context.Settings.Remove(settings);
            _isDirty = true;
        }
    }

    public void SaveChanges()
    {
        _context.SaveChanges();
        _isDirty = true;
    }
}