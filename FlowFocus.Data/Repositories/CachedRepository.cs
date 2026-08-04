using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data.Repositories;

public abstract class CachedRepository<T>(StorageContext context, INotificationService notificationService) : IRepository<T>
    where T : class, IAuditEntity
{
    private List<T>? _cache;
    private bool _isDirty = true;
    protected readonly object CacheLock = new();
    protected readonly StorageContext Context = context;
    protected readonly INotificationService NotificationService = notificationService;

    protected abstract DbSet<T> GetDbSet();

    protected virtual IQueryable<T> GetBaseQuery() =>
        GetDbSet().AsNoTracking().AsQueryable();

    protected virtual IQueryable<T> GetTrackedQuery() =>
        GetDbSet().AsQueryable();

    public virtual List<T> GetAll()
    {
        lock (CacheLock)
        {
            if (_isDirty || _cache is null)
            {
                _cache = GetBaseQuery().ToList();
                _isDirty = false;
            }

            return _cache.ToList();
        }
    }

    public virtual T? GetById(int id)
    {
        return GetAll().FirstOrDefault(e => e.Id == id);
    }

    private T? GetTrackedById(int id)
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

    protected int GetNextId()
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

    protected void UpdatePartial(int id, Action<T> updateAction, bool saveChanges = true)
    {
        lock (CacheLock)
        {
            var entity = GetTrackedById(id);
            if (entity == null) return;
            updateAction(entity);
            entity.LastChangesOn = DateTime.UtcNow;

            if (_cache != null)
            {
                var cachedEntity = _cache.FirstOrDefault(e => e.Id == id);
                if (cachedEntity != null)
                {
                    updateAction(cachedEntity);
                    cachedEntity.LastChangesOn = entity.LastChangesOn;
                }
            }

            if (saveChanges)
            {
                Context.SaveChanges();
                MarkDirty();
            }
        }
    }

    protected void MarkDirty()
    {
        _isDirty = true;
        NotificationService?.NotifyTasksChanged();
    }

    protected void RefreshCache()
    {
        lock (CacheLock)
        {
            _isDirty = true;
            _cache = null;
        }
    }

    // Реализации методов интерфейса IRepository, требуемые компилятором
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

    public void SaveChangesAndNotify()
    {
        lock (CacheLock)
        {
            Context.SaveChanges();
            MarkDirty();
        }
    }
}