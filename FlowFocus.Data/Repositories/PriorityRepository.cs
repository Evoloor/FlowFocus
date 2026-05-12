using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Репозиторий приоритетов
/// </summary>
public class PriorityRepository(StorageContext context, INotificationService notificationService) : CachedRepository<PriorityLevel>(context, notificationService), IPriorityRepository
{
    protected override DbSet<PriorityLevel> GetDbSet() => Context.Priorities;

    public List<PriorityLevel> GetAllOrdered()
    {
        return GetAll().OrderBy(p => p.Order).ToList();
    }

    public PriorityLevel? GetHighestPriority()
    {
        return GetAllOrdered().FirstOrDefault();
    }

    public List<PriorityLevel> GetPrioritiesHigherThan(int priorityId)
    {
        var priority = GetById(priorityId);
        if (priority == null) return GetAllOrdered();

        return GetAll()
            .Where(p => p.Order < priority.Order)
            .OrderBy(p => p.Order)
            .ToList();
    }

    public void Reorder(List<int> orderedIds)
    {
        lock (CacheLock)
        {
            var priorities = Context.Priorities.ToList();
            for (var i = 0; i < orderedIds.Count; i++)
            {
                var priority = priorities.FirstOrDefault(p => p.Id == orderedIds[i]);
                if (priority != null)
                {
                    priority.Order = i + 1;
                    priority.LastChangesOn = DateTime.UtcNow;
                }
            }
            Context.SaveChanges();
            MarkDirty();
        }
    }
}