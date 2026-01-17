using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data;

/// <summary>
/// Сервис алгоритмического планирования задач
/// </summary>
public class PlannerService(
    ITaskRepository taskRepository,
    StorageContext context) : IPlannerService
{
    /// <summary>
    /// Шаг 1: Актуализация приоритетов на основе таблиц повышения
    /// </summary>
    public void ActualizePriorities(int dayStartHour)
    {
        var logicalToday = DateHelper.GetLogicalToday(dayStartHour);
        var tasks = taskRepository.GetAll();

        foreach (var task in tasks)
        {
            if (task.Status is TaskStatus.Completed or TaskStatus.Irrelevant)
                continue;

            // Не повышаем приоритеты для повторяющихся задач (это может приводить к постоянному изменению EffectivePriority у копий)
            if (task.IsRecurring)
                continue;
            
            var escalations = task.PriorityEscalations
                .Where(e => !e.IsApplied && e.EscalationDate.Date <= logicalToday)
                .OrderBy(e => e.TargetPriority?.Order ?? 99)
                .ToList();

            if (escalations.Count != 0)
            {
                var highestEscalation = escalations.First();
                
                // Обновляем эффективный приоритет
                var trackedTask = context.Tasks.Find(task.Id);
                if (trackedTask != null)
                {
                    trackedTask.EffectivePriorityId = highestEscalation.TargetPriorityId;
                    trackedTask.LastChangesOn = DateTime.UtcNow;
                }

                // Помечаем повышения как применённые
                foreach (var escalation in escalations)
                {
                    var trackedEscalation = context.PriorityEscalations.Find(escalation.Id);
                    if (trackedEscalation != null)
                    {
                        trackedEscalation.IsApplied = true;
                        trackedEscalation.LastChangesOn = DateTime.UtcNow;
                    }
                }
            }
        }

        taskRepository.SaveChanges();
    }

    /// <summary>
    /// Шаг 2: Распределение задач по дням
    /// </summary>
    public void DistributeTasks(UserSettings settings)
    {
        // Сначала обрабатываем повторяющиеся задачи: если у них нет ручной даты и они просрочены/неназначены,
        // назначаем ближайшую дату повторения или сегодня, если ближайшая уже просрочена.
        HandleRecurringBeforeDistribution(settings);

        var tasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null) // Исключаем подзадачи
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.UserAssignedDate == null) // Только задачи без ручной даты
            // Исключаем повторяющиеся задачи и реплики, чтобы их даты управлялись правилами повторения
            .Where(t => !t.IsRecurring && t.RecurrenceSourceId == null)
            .ToList();

        // Сортировка по релевантности
        var sortedTasks = tasks
            .OrderBy(GetEffectivePriorityOrder)
            .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1) // Короткие первые
            .ThenByDescending(t => t.Interest ?? 0)
            .ToList();

        var currentDate = DateHelper.GetLogicalToday(settings.DayStartHour);
        var dailyStats = new DailyStats();

        foreach (var task in sortedTasks)
        {
            var isLargeTask = IsLargeTask(task, settings);

            // Проверяем лимиты
            while (!CanAddToDay(task, dailyStats, settings, isLargeTask))
            {
                currentDate = currentDate.AddDays(1);
                dailyStats = new();
            }

            // Назначаем задачу на текущий день
            var trackedTask = context.Tasks.Find(task.Id);
            if (trackedTask != null)
            {
                trackedTask.ActualAssignedDate = currentDate;
                trackedTask.LastChangesOn = DateTime.UtcNow;
                Console.WriteLine($"Planner: assigned non-recurring task {task.Id} '{task.Title}' -> {currentDate:yyyy-MM-dd}");
            }

            // Обновляем статистику дня (подзадачи не учитываются в счётчике задач)
            dailyStats.TotalComplexity += task.TotalComplexity;
            dailyStats.TotalMinutes += task.TotalEstimatedMinutes;
            dailyStats.TaskCount++;
        }

        // Обрабатываем задачи с ручной датой
        var fixedDateTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.UserAssignedDate != null)
            // Не перезаписываем даты для повторяющихся задач / реплик
            .Where(t => !t.IsRecurring && t.RecurrenceSourceId == null)
            .ToList();

        foreach (var task in fixedDateTasks)
        {
            var trackedTask = context.Tasks.Find(task.Id);
            if (trackedTask == null) continue;
            trackedTask.ActualAssignedDate = task.UserAssignedDate;
            trackedTask.LastChangesOn = DateTime.UtcNow;
        }

        taskRepository.SaveChanges();
    }

    /// <summary>
    /// Полный пересчёт
    /// </summary>
    public void RecalculateAll(UserSettings settings)
    {
        ActualizePriorities(settings.DayStartHour);
        DistributeTasks(settings);
        UpdateBlockedStatuses();
    }

    /// <summary>
    /// Обновление статусов заблокированных задач
    /// </summary>
    public void UpdateBlockedStatuses()
    {
        var tasks = taskRepository.GetAll();

        foreach (var task in tasks)
        {
            if (task.Status is TaskStatus.Completed or TaskStatus.Irrelevant)
                continue;

            // Собираем всех активных блокеров как из обратных связей Blocks (Source -> this)
            var blockers = new List<TaskItem?>();

            blockers.AddRange(task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => r.SourceTask));

            var hasActiveBlockers = blockers.Any(b => b != null && b.Status != TaskStatus.Completed && b.Status != TaskStatus.Irrelevant);

            var trackedTask = context.Tasks.Find(task.Id);
            if (trackedTask != null)
            {
                // Если есть активные блокеры, устанавливаем статус Blocked
                if (hasActiveBlockers && trackedTask.Status != TaskStatus.Blocked)
                {
                    trackedTask.Status = TaskStatus.Blocked;
                    trackedTask.LastChangesOn = DateTime.UtcNow;
                }
                // Если нет активных блокеров, но статус Blocked, возвращаем к Planned
                else if (!hasActiveBlockers && trackedTask.Status == TaskStatus.Blocked)
                {
                    trackedTask.Status = TaskStatus.Planned;
                    trackedTask.LastChangesOn = DateTime.UtcNow;
                }
            }
        }

        taskRepository.SaveChanges();
    }

    private int GetEffectivePriorityOrder(TaskItem task)
    {
        return task.EffectivePriority?.Order ?? task.Priority?.Order ?? 99;
    }

    private bool IsLargeTask(TaskItem task, UserSettings settings)
    {
        var timeThreshold = settings.DailyTimeLimit * AppConfig.LargeTaskThresholdPercent;
        var complexityThreshold = settings.DailyComplexityLimit * AppConfig.LargeTaskThresholdPercent;

        return task.TotalEstimatedMinutes >= timeThreshold ||
               task.TotalComplexity >= complexityThreshold;
    }

    private bool CanAddToDay(TaskItem task, DailyStats stats, UserSettings settings, bool isLargeTask)
    {
        // Крупные дела игнорируют лимит
        if (isLargeTask) return true;

        if (stats.TaskCount >= settings.DailyTaskLimit) return false;
        if (stats.TotalComplexity + task.TotalComplexity > settings.DailyComplexityLimit) return false;
        if (stats.TotalMinutes + task.TotalEstimatedMinutes > settings.DailyTimeLimit) return false;

        return true;
    }

    /// <summary>
    /// Обработать повторяющиеся задачи перед основным распределением: назначить им ближайшую по правилу даты повторения
    /// или, если следующая дата меньше или равна сегодняшней, назначить на сегодня.
    /// </summary>
    private void HandleRecurringBeforeDistribution(UserSettings settings)
    {
        var logicalToday = DateHelper.GetLogicalToday(settings.DayStartHour);

        var allRecurring = context.Tasks
            .AsNoTracking()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.IsRecurring)
            .ToList();

        // Оставляем только те повторы, которые просрочены по ActualAssignedDate или UserAssignedDate, либо ещё не назначены
        var recurringTasks = allRecurring.Where(t =>
                DateHelper.IsOverdue(t.ActualAssignedDate, settings.DayStartHour) ||
                DateHelper.IsOverdue(t.UserAssignedDate, settings.DayStartHour) ||
                (t.ActualAssignedDate == null && t.UserAssignedDate == null)
            ).ToList();

        Console.WriteLine($"Planner: found {recurringTasks.Count} recurring candidates for handling");
        foreach (var c in recurringTasks)
        {
            Console.WriteLine($"Planner: candidate {c.Id} '{c.Title}' RecType={c.RecurrenceType} IsRecurring={c.IsRecurring} Actual={c.ActualAssignedDate?.ToString("yyyy-MM-dd") ?? "<null>"} User={c.UserAssignedDate?.ToString("yyyy-MM-dd") ?? "<null>"} Completed={c.CompletedDate?.ToString("yyyy-MM-dd") ?? "<null>"}");
        }

        foreach (var task in recurringTasks)
        {
            // Уважать ручную дату, если пользователь её установил
            // Если у задачи есть ручная дата, и она не просрочена (т.е. сегодня или в будущем), ничего не делаем.
            // Если же ручная дата в прошлом, нам нужно продвинуть задачу по правилу повтора.
            if (task.UserAssignedDate != null && !DateHelper.IsOverdue(task.UserAssignedDate, settings.DayStartHour))
                continue;

            // Если задача уже назначена на сегодня или в будущем — ничего не делаем
            if (task.ActualAssignedDate != null && !DateHelper.IsOverdue(task.ActualAssignedDate, settings.DayStartHour))
                continue;

            // Вычисляем базовую дату для расчёта следующей повторки: используем логику для завершённых задач как в репозитории;
            // для незавершённых — используем последнюю назначенную дату (ActualAssignedDate/UserAssignedDate) или вчера, если не задано.
            DateTime baseCandidate;
            if (task.CompletedDate.HasValue)
            {
                // если задача была завершена — используем логику репозитория
                var completedDate = task.CompletedDate.Value;
                var assignedDate = task.UserAssignedDate ?? completedDate;
                baseCandidate = completedDate.Date >= assignedDate.Date ? completedDate.Date : assignedDate.Date;
            }
            else
            {
                // незавершённая задача: берём последнюю назначенную дату как базу, чтобы пройти серией missed occurrences
                if (task.ActualAssignedDate.HasValue)
                    baseCandidate = task.ActualAssignedDate.Value.Date;
                else if (task.UserAssignedDate.HasValue)
                    baseCandidate = task.UserAssignedDate.Value.Date;
                else
                    // если у повторяющейся задачи нет предыдущей даты, поставим базу вчера, чтобы nextDate мог быть сегодня
                    baseCandidate = DateTime.UtcNow.Date.AddDays(-1);
            }

            // Продвигаем по правилу повтора, пока не дойдём до даты >= logicalToday (защита итераций)
            var nextDate = GetNextRecurrenceDateFromBase(task, baseCandidate);
            if (nextDate == null)
                continue;

            var guard = 0;
            while (nextDate.HasValue && nextDate.Value.Date < logicalToday.Date && guard < 365)
            {
                // сдвигаем базу на найденную дату и считаем следующую
                baseCandidate = nextDate.Value.Date;
                nextDate = GetNextRecurrenceDateFromBase(task, baseCandidate);
                guard++;
            }

            DateTime assigned;
            if (nextDate.HasValue && nextDate.Value.Date >= logicalToday.Date)
                assigned = nextDate.Value.Date;
            else
                // если по какой-то причине не получилось получить будущую дату, назначаем на сегодня
                assigned = logicalToday;

            var trackedTask = context.Tasks.Find(task.Id);
            if (trackedTask != null)
            {
                // Устанавливаем и UserAssignedDate (если не было), и ActualAssignedDate — это назначение по правилу повтора
                if (trackedTask.UserAssignedDate == null)
                    trackedTask.UserAssignedDate = assigned;
                trackedTask.ActualAssignedDate = assigned;
                trackedTask.LastChangesOn = DateTime.UtcNow;
                Console.WriteLine($"Planner: recurring task {task.Id} '{task.Title}' moved from Actual={task.ActualAssignedDate?.ToString("yyyy-MM-dd") ?? "<null>"} User={task.UserAssignedDate?.ToString("yyyy-MM-dd") ?? "<null>"} -> Assigned={assigned:yyyy-MM-dd}");
            }
        }

        taskRepository.SaveChanges();
    }

    // Новая реализация: рассчитывает следующую дату повтора, считая от baseDate
    private DateTime? GetNextRecurrenceDateFromBase(TaskItem task, DateTime baseDate)
    {
        return task.RecurrenceType switch
        {
            RecurrenceType.Daily => baseDate.AddDays(1),
            RecurrenceType.EveryNDays => baseDate.AddDays(task.RecurrenceInterval ?? 1),
            RecurrenceType.WeekDays => CalculateNextWeekDayDate(baseDate, task.RecurrenceWeekDays ?? 0),
            RecurrenceType.Monthly => CalculateNextMonthDate(baseDate, task.RecurrenceInterval ?? 1),
            RecurrenceType.Yearly => CalculateNextYearDate(baseDate, task.RecurrenceInterval ?? 1),
            _ => null
        };
    }

    private DateTime CalculateNextMonthDate(DateTime baseDate, int monthsInterval)
    {
        if (monthsInterval <= 0) monthsInterval = 1;
        var target = baseDate.AddMonths(monthsInterval);
        var day = baseDate.Day;
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        var chosenDay = day > daysInTarget ? daysInTarget : day;
        return new DateTime(target.Year, target.Month, chosenDay);
    }

    private DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        if (yearsInterval <= 0) yearsInterval = 1;
        var targetYear = baseDate.Year + yearsInterval;
        var month = baseDate.Month;
        var day = baseDate.Day;

        var daysInTarget = DateTime.DaysInMonth(targetYear, month);
        var chosenDay = day > daysInTarget ? daysInTarget : day;
        return new DateTime(targetYear, month, chosenDay);
    }

    private DateTime CalculateNextWeekDayDate(DateTime baseDate, int weekDaysMask)
    {
        var currentDay = (int)baseDate.DayOfWeek;
        // Ищем следующий день недели из маски (DayOfWeek: 0=Sunday)
        for (var i = 1; i <= 7; i++)
        {
            var nextDay = (currentDay + i) % 7;
            var nextMaskDay = nextDay == 0 ? 64 : 1 << (nextDay - 1);

            if ((weekDaysMask & nextMaskDay) != 0)
            {
                return baseDate.AddDays(i);
            }
        }

        // Если не нашли, возвращаем через неделю
        return baseDate.AddDays(7);
    }

    private class DailyStats
    {
        public int TotalComplexity { get; set; }
        public int TotalMinutes { get; set; }
        public int TaskCount { get; set; }
    }
}
