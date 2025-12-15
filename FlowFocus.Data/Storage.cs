using FlowFocus.Core;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    public DbSet<TaskItem> Tasks { get; set; } = null!;
    public DbSet<PriorityLevel> Priorities { get; set; } = null!;
    public DbSet<Tag> Tags { get; set; } = null!;
    public DbSet<TaskTag> TaskTags { get; set; } = null!;
    public DbSet<TaskRelation> TaskRelations { get; set; } = null!;
    public DbSet<PriorityEscalation> PriorityEscalations { get; set; } = null!;
    public DbSet<UserSettings> Settings { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=flowfocus.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TaskItem
        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.ParentTask)
                .WithMany(e => e.Subtasks)
                .HasForeignKey(e => e.ParentTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Priority)
                .WithMany()
                .HasForeignKey(e => e.PriorityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.EffectivePriority)
                .WithMany()
                .HasForeignKey(e => e.EffectivePriorityId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Tags)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Relations)
                .WithOne(e => e.SourceTask)
                .HasForeignKey(e => e.SourceTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.InverseRelations)
                .WithOne(e => e.TargetTask)
                .HasForeignKey(e => e.TargetTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.PriorityEscalations)
                .WithOne(e => e.Task)
                .HasForeignKey(e => e.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // TaskRelation
        modelBuilder.Entity<TaskRelation>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // TaskTag
        modelBuilder.Entity<TaskTag>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.Tag)
                .WithMany()
                .HasForeignKey(e => e.TagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // PriorityLevel
        modelBuilder.Entity<PriorityLevel>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Tag
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // PriorityEscalation
        modelBuilder.Entity<PriorityEscalation>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TargetPriority)
                .WithMany()
                .HasForeignKey(e => e.TargetPriorityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // UserSettings
        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        // Seed default priorities
        modelBuilder.Entity<PriorityLevel>().HasData(
            new PriorityLevel { Id = 1, Order = 1, Name = "Критический", Color = "#FF4444", IsSystem = true },
            new PriorityLevel { Id = 2, Order = 2, Name = "Высокий", Color = "#FF8C00", IsSystem = true },
            new PriorityLevel { Id = 3, Order = 3, Name = "Средний", Color = "#FFD700", IsSystem = true },
            new PriorityLevel { Id = 4, Order = 4, Name = "Низкий", Color = "#4CAF50", IsSystem = true },
            new PriorityLevel { Id = 5, Order = 5, Name = "Фоновый", Color = "#2196F3", IsSystem = true }
        );

        // Seed default settings
        modelBuilder.Entity<UserSettings>().HasData(
            new UserSettings
            {
                Id = 1,
                DayStartHour = 5,
                DailyComplexityLimit = 100,
                DailyTimeLimit = 480,
                DailyTaskLimit = 10,
                AutoDistributeEnabled = false,
                IsDarkMode = true,
                HideTaskTitlesDefault = false
            }
        );
    }
}

public abstract class CachedRepository<T>(StorageContext context) : IRepository<T>
    where T : class, IAuditEntity
{
    protected List<T>? Cache;
    protected bool IsDirty = true;
    protected readonly object CacheLock = new();
    protected readonly StorageContext Context = context;

    protected abstract DbSet<T> GetDbSet();

    protected virtual IQueryable<T> GetBaseQuery() =>
        GetDbSet().AsNoTracking().AsQueryable();

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

            return Cache.ToList();
        }
    }

    public virtual T? GetById(int id)
    {
        return GetAll().FirstOrDefault(e => e.Id == id);
    }

    protected T? GetTrackedById(int id)
    {
        if (id > 0) return GetTrackedQuery().FirstOrDefault(e => e.Id == id);
        Console.WriteLine($"Warning: Attempting to get entity with invalid ID: {id}");
        return null;
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
            Context.SaveChanges();
            MarkDirty();
        }
    }

    private int GetNextId()
    {
        var maxId = Context.Set<T>()
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
                if (entity.Id <= 0)
                {
                    throw new InvalidOperationException(
                        $"Invalid entity ID: {entity.Id}. Entity must have a valid ID > 0 for update.");
                }

                var trackedEntity = GetTrackedById(entity.Id);
                if (trackedEntity == null)
                {
                    throw new InvalidOperationException($"Entity with ID {entity.Id} not found");
                }

                Context.Entry(trackedEntity).CurrentValues.SetValues(entity);
                trackedEntity.LastChangesOn = DateTime.UtcNow;

                Context.SaveChanges();
                MarkDirty();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Update: {ex.Message}");
                throw;
            }
        }
    }

    protected void UpdatePartial(int id, Action<T> updateAction)
    {
        lock (CacheLock)
        {
            var entity = GetTrackedById(id);
            if (entity == null) return;
            updateAction(entity);
            entity.LastChangesOn = DateTime.UtcNow;
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public virtual void Delete(int id)
    {
        lock (CacheLock)
        {
            var entity = GetTrackedQuery().FirstOrDefault(e => e.Id == id);
            if (entity == null) return;
            GetDbSet().Remove(entity);
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void SaveChanges()
    {
        Context.SaveChanges();
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
