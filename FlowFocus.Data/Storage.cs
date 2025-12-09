using System.Text.Json;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

public class StorageContext : DbContext
{
    public DbSet<TaskItem> Tasks { get; set; }
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=flowfocus.db");
        }
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

                // Проверка на валидный ID
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
