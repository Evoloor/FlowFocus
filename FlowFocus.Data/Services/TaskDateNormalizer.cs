using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories.Helpers;
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
        var today = TodoDay.Today;
        var todayDt = today.ToDateTime();

        var activeTasks = context.Tasks
            .WhereActive()
            .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
            .ToList();

        var inactiveConditionIds = context.ExternalConditions
            .AsNoTracking()
            .Where(c => !c.IsActive)
            .Select(c => c.Id)
            .ToHashSet();

        bool HasInactiveCondition(TaskItem t) =>
            t.Conditions.Any(c => (c.Condition != null && !c.Condition.IsActive) || inactiveConditionIds.Contains(c.ConditionId));

        // 0. Нормализация задач, у которых было отключено повторение:
        // Если у активной задачи отключено повторение (!IsRecurring), но сохранились признаки серии повторений (RecurrenceSourceId != null или RecurrenceType != None):
        // - Отвязываем её от серии повторений (RecurrenceSourceId = null) и сбрасываем параметры повтора.
        // - Если статус даты был AutoFixed (унаследованный от повторений), переводим в AutoFlexible, чтобы задача не висела вечным якорем.
        var recurrenceDetachedTasks = activeTasks
            .Where(t => !t.IsRecurring && (t.RecurrenceSourceId != null || t.RecurrenceType != RecurrenceType.None))
            .ToList();

        if (recurrenceDetachedTasks.Count > 0)
        {
            foreach (var task in recurrenceDetachedTasks)
            {
                task.ResetRecurrence();
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        // 1. Нормализация неназначенных дат для обычных (не повторяющихся) задач:
        // Если ScheduledDate == null и задача не повторяющаяся, её статус должен быть AutoFlexible
        var tasksToNormalize = activeTasks
            .Where(t => t.ScheduledDate == null && !t.IsRecurring && t.RecurrenceSourceId == null && (t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed))
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

        // 1.1. Нормализация повторяющихся задач со статусом AutoFlexible:
        // Повторяющиеся задачи не могут иметь статус AutoFlexible — они переводятся в AutoFixed.
        var recurringAutoFlexibleTasks = activeTasks
            .Where(t => t.IsRecurring && t.DateSource == DateSource.AutoFlexible)
            .ToList();

        if (recurringAutoFlexibleTasks.Count > 0)
        {
            foreach (var task in recurringAutoFlexibleTasks)
            {
                task.DateSource = DateSource.AutoFixed;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        // 1.2. Нормализация просроченных задач с ручной датой:
        // Просроченная задача с DateSource.Manual переводится в AutoFlexible для обычных задач и AutoFixed для повтор-задач.
        var overdueManualTasks = activeTasks
            .Where(t => t.DateSource == DateSource.Manual)
            .Where(t => today.IsOverdue(t.ScheduledDate))
            .ToList();

        if (overdueManualTasks.Count > 0)
        {
            foreach (var task in overdueManualTasks)
            {
                task.DateSource = task.IsRecurring ? DateSource.AutoFixed : DateSource.AutoFlexible;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        // 1.3. Сброс дат для заблокированных неактивным условием повторяющихся задач ("улетают" из расписания)
        var blockedRecurringTasks = activeTasks
            .Where(t => t.IsRecurring)
            .Where(t => t.DateSource != DateSource.Manual)
            .Where(HasInactiveCondition)
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

        // Собираем из базы завершённые задачи для анализа серии повторений без N+1 запросов
        var lastCompletedDates = context.Tasks
            .AsNoTracking()
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate != null)
            .ToList()
            .GroupBy(t => t.RecurrenceSourceId ?? t.Id)
            .ToDictionary(g => g.Key, g => g.Max(t => t.CompletedDate!.Value));

        var activeRecurringTasks = activeTasks
            .Where(t => t.IsRecurring)
            .Where(t => t.DateSource != DateSource.Manual)
            .Where(t => !HasInactiveCondition(t))
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
            var targetDate = (nextDate == null || nextDate.Value.Date <= todayDt.Date)
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

        // 2.1. Гарантия инварианта: если активная задача не заблокирована неактивным условием и имеет статус AutoFixed,
        // то её ScheduledDate после нормализации должен быть непустым.
        var unassignedAutoFixed = activeTasks
            .Where(t => t.DateSource == DateSource.AutoFixed && t.ScheduledDate == null)
            .Where(t => !HasInactiveCondition(t))
            .ToList();

        if (unassignedAutoFixed.Count > 0)
        {
            foreach (var task in unassignedAutoFixed)
            {
                task.ScheduledDate = todayDt;
                task.LastChangesOn = DateTime.UtcNow;
            }
            hasChanges = true;
        }

        return hasChanges;
    }
}
