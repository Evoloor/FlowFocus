using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

/// <summary>
/// Сервис нормализации источников дат задач
/// </summary>
public static class TaskDateNormalizer
{
    public static bool NormalizeDateSources(StorageContext context, ITaskRecurrenceService recurrenceService)
    {
        var hasChanges = false;

        // 1. Нормализация неназначенных дат: если ScheduledDate == null и DateSource не AutoFlexible
        var tasksToNormalize = context.Tasks
            .Where(t => t.ScheduledDate == null && (t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed))
            .ToList();

        if (tasksToNormalize.Count > 0)
        {
            foreach (var task in tasksToNormalize)
            {
                task.DateSource = DateSource.AutoFlexible;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        var today = TodoDay.Today;

        // 1.2. Нормализация просроченных задач с ручной датой:
        // Просроченная задача с DateSource.Manual переводится в AutoFlexible для перераспределения.
        var overdueManualTasks = context.Tasks
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual)
            .ToList()
            .Where(t => today.IsOverdue(t.ScheduledDate))
            .ToList();

        if (overdueManualTasks.Count > 0)
        {
            foreach (var task in overdueManualTasks)
            {
                task.DateSource = DateSource.AutoFlexible;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        // 1.3. Сброс дат для заблокированных неактивным условием повторяющихся задач ("улетают" из расписания)
        var blockedRecurringTasks = context.Tasks
            .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.IsRecurring || t.RecurrenceSourceId != null)
            .Where(t => t.DateSource != DateSource.Manual)
            .Where(t => t.Conditions.Any(c => c.Condition != null && !c.Condition.IsActive))
            .Where(t => t.ScheduledDate != null)
            .ToList();

        if (blockedRecurringTasks.Count > 0)
        {
            foreach (var task in blockedRecurringTasks)
            {
                task.ScheduledDate = null;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        var todayDt = today.ToDateTime();

        // Собираем из базы все задачи для анализа серии повторений без N+1 запросов
        var allTasks = context.Tasks.AsNoTracking().ToList();

        var lastCompletedDates = allTasks
            .Where(t => t is { Status: TaskStatus.Completed, CompletedDate: not null })
            .GroupBy(t => t.RecurrenceSourceId ?? t.Id)
            .ToDictionary(g => g.Key, g => g.Max(t => t.CompletedDate!.Value));

        var activeRecurringTasks = context.Tasks
            .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.IsRecurring || t.RecurrenceSourceId != null)
            .Where(t => t.DateSource != DateSource.Manual)
            .Where(t => !t.Conditions.Any(c => c.Condition != null && !c.Condition.IsActive))
            .ToList();

        foreach (var task in activeRecurringTasks)
        {
            var sourceId = task.RecurrenceSourceId ?? task.Id;
            DateTime? nextDate = null;

            if (lastCompletedDates.TryGetValue(sourceId, out var lastCompleted))
            {
                nextDate = recurrenceService.CalculateNextRecurrenceDateFromBase(task, lastCompleted);
            }

            // Если задача не выполнялась вовсе или рассчитанный очередной срок <= today, значит просрочена/должна выполняться сегодня
            DateTime targetDate = (nextDate == null || nextDate.Value.Date <= todayDt.Date)
                ? todayDt
                : nextDate.Value.Date;

            if (task.ScheduledDate != targetDate || task.DateSource != DateSource.AutoFixed)
            {
                task.ScheduledDate = targetDate;
                task.DateSource = DateSource.AutoFixed;
                task.LastChangesOn = DateTime.UtcNow;
                hasChanges = true;
            }
        }

        return hasChanges;
    }
}
