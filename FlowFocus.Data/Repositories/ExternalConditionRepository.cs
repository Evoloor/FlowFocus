using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Репозиторий внешних условий
/// </summary>
public class ExternalConditionRepository(
    StorageContext context,
    INotificationService notificationService,
    ITaskRecurrenceService? recurrenceService = null)
    : CachedRepository<ExternalCondition>(context, notificationService), IExternalConditionRepository
{
    private readonly ITaskRecurrenceService _recurrenceService = recurrenceService ?? new TaskRecurrenceService();

    protected override DbSet<ExternalCondition> GetDbSet() => Context.ExternalConditions;

    public ExternalCondition? GetByName(string name)
    {
        return GetAll().FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public ExternalCondition GetOrCreate(string name)
    {
        var existing = GetByName(name);
        if (existing != null) return existing;

        ExternalCondition condition = new()
        {
            Name = name,
            BackgroundColor = GeneratePastelColor(),
            IsActive = false
        };
        Add(condition);
        return condition;
    }

    public List<ExternalCondition> SearchByName(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return GetAll()
            .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.UsageCount)
            .Take(limit)
            .ToList();
    }

    public void ToggleConditionActive(int conditionId, bool isActive)
    {
        lock (CacheLock)
        {
            var condition = Context.ExternalConditions.Find(conditionId);
            if (condition == null) return;

            condition.IsActive = isActive;
            condition.LastChangesOn = DateTime.UtcNow;
            Context.SaveChanges();

            // Выполняем нормализацию дат (назначение дат для активированных и сброс для деактивированных задач)
            TaskDateNormalizer.NormalizeDateSources(Context, _recurrenceService);
            Context.SaveChanges();

            // Пересчитываем статусы всех заблокированных/запланированных задач
            var planner = new PriorityEscalationPlanner(new TaskRepository(Context, NotificationService, _recurrenceService));
            planner.UpdateBlockedStatuses();

            Context.SaveChanges();
            MarkDirty();
            NotificationService.NotifySettingsChanged();
            NotificationService.NotifyTasksChanged();
        }
    }

    public void DeleteCondition(int conditionId)
    {
        lock (CacheLock)
        {
            var condition = Context.ExternalConditions.Find(conditionId);
            if (condition == null) return;

            var taskConditions = Context.TaskConditions.Where(tc => tc.ConditionId == conditionId).ToList();
            if (taskConditions.Count > 0)
            {
                Context.TaskConditions.RemoveRange(taskConditions);
            }

            Context.ExternalConditions.Remove(condition);
            Context.SaveChanges();

            // Пересчитываем статусы освободившихся задач
            var planner = new PriorityEscalationPlanner(new TaskRepository(Context, NotificationService, _recurrenceService));
            planner.UpdateBlockedStatuses();

            Context.SaveChanges();
            MarkDirty();
            NotificationService.NotifySettingsChanged();
            NotificationService.NotifyTasksChanged();
        }
    }

    public void IncrementUsage(int conditionId)
    {
        UpdatePartial(conditionId, c =>
        {
            c.UsageCount++;
            c.LastUsedDate = DateTime.UtcNow;
        });
    }

    public void DecrementUsage(int conditionId)
    {
        if (conditionId <= 0) return;

        lock (CacheLock)
        {
            var condition = Context.ExternalConditions.Find(conditionId);
            if (condition == null) return;

            condition.UsageCount = Math.Max(0, condition.UsageCount - 1);
            Context.SaveChanges();
            MarkDirty();
        }
    }

    private static readonly string[] PastelColors =
    [
        "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9", "#BAE1FF",
        "#E0BBE4", "#FEC8D8", "#D4F0F0", "#CCE2CB", "#B6CFB6",
        "#97C1A9", "#FCB9AA", "#FFDBCC", "#ECEAE4", "#A2E1DB"
    ];

    private static string GeneratePastelColor()
    {
        return PastelColors[Random.Shared.Next(PastelColors.Length)];
    }
}
