using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

/// <summary>
/// Сервис алгоритмического планирования задач
/// </summary>
public class PlannerService(ITaskRepository taskRepository) : IPlannerService
{
    /// <summary>
    /// Шаг 1: Актуализация приоритетов на основе таблиц повышения
    /// </summary>
    public void ActualizePriorities()
    {
        var today = TodoDay.Today;
        var tasks = taskRepository.GetAll();

        foreach (var task in tasks)
        {
            if (task.Status is TaskStatus.Completed or TaskStatus.Irrelevant)
                continue;

            // Не повышаем приоритеты для повторяющихся задач (это может приводить к постоянному изменению EffectivePriority у копий)
            if (task.IsRecurring)
                continue;
            
            var escalations = task.PriorityEscalations
                .Where(e => !e.IsApplied && e.EscalationDate.Date <= today.Date)
                .OrderBy(e => e.TargetPriority?.Order ?? 99)
                .ToList();

            if (escalations.Count == 0) continue;
            var highestEscalation = escalations.First();
                
            taskRepository.ApplyPriorityEscalation(
                task.Id,
                highestEscalation.TargetPriorityId,
                escalations.Select(e => e.Id),
                saveChanges: true
            );
        }

        NormalizeBlockerPriorities();
    }

    /// <summary>
    /// Нормализация приоритетов блокеров:
    /// Если у заблокированной задачи приоритет выше (меньший Order), чем у её блокера,
    /// блокер каскадно повышает свой приоритет до уровня заблокированной задачи.
    /// </summary>
    public void NormalizeBlockerPriorities()
    {
        var tasks = taskRepository.GetAll()
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant)
            .ToList();

        var taskMap = tasks.ToDictionary(t => t.Id);

        bool changed;
        do
        {
            changed = false;
            foreach (var task in tasks)
            {
                var targetOrder = GetEffectivePriorityOrder(task);
                if (!task.PriorityId.HasValue) continue;

                foreach (var relation in task.InverseRelations.Where(r => r.Type == RelationType.Blocks))
                {
                    if (!taskMap.TryGetValue(relation.SourceTaskId, out var blockerTask)) continue;
                    if (blockerTask.Status is TaskStatus.Completed or TaskStatus.Irrelevant) continue;

                    var blockerOrder = GetEffectivePriorityOrder(blockerTask);
                    if (targetOrder < blockerOrder)
                    {
                        blockerTask.PriorityId = task.PriorityId.Value;
                        taskRepository.Update(blockerTask);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                taskRepository.SaveChanges();
                tasks = taskRepository.GetAll()
                    .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant)
                    .ToList();
                taskMap = tasks.ToDictionary(t => t.Id);
            }
        } while (changed);
    }

    /// <summary>
    /// Нормализация перед перераспределением:
    /// Задачи со статусом источника даты Manual или AutoFixed, у которых не указана дата (ScheduledDate == null),
    /// приводятся к источнику AutoFlexible.
    /// </summary>
    private void NormalizeTaskDateSources()
    {
        taskRepository.NormalizeTaskDateSources(saveChanges: false);
    }

    /// <summary>
    /// Шаг 2: Распределение задач по дням.
    /// Перераспределяются только задачи с DateSource == AutoFlexible.
    /// Задачи с Manual или AutoFixed остаются на своих датах.
    /// Детальный просчёт выполняется только для "сегодня" и "завтра". Оставшиеся задачи сбросят ScheduledDate = null.
    /// </summary>
    public void DistributeTasks(UserSettings settings)
    {
        // Сначала обрабатываем повторяющиеся задачи: если они просрочены/неназначены,
        // мутируем их на месте (Scenario B из спецификации).
        HandleRecurringBeforeDistribution(settings);

        var allTasksMap = taskRepository.GetAll().ToDictionary(t => t.Id);

        // Автоматически фиксируем (AutoFixed) даты для блокеров, если заблокированные задачи имеют фиксированную дату (Manual / AutoFixed)
        var unassignedBlockers = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            .ToList();

        foreach (var blocker in unassignedBlockers)
        {
            var blockedWithFixedDate = blocker.Relations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => allTasksMap.TryGetValue(r.TargetTaskId, out var target) ? target : r.TargetTask)
                .Where(t => t != null && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
                .Where(t => t!.DateSource is DateSource.Manual or DateSource.AutoFixed && t.ScheduledDate.HasValue)
                .OrderBy(t => t!.ScheduledDate!.Value)
                .FirstOrDefault();

            if (blockedWithFixedDate != null)
            {
                var targetDate = blockedWithFixedDate.ScheduledDate!.Value;
                taskRepository.UpdateTaskSchedule(blocker.Id, targetDate, DateSource.AutoFixed, saveChanges: false);
            }
        }

        // Задачи с AutoFlexible — кандидаты для авто-распределения
        var tasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null) // Исключаем подзадачи
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            // Только задачи, которые плановик может свободно перемещать
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            // Исключаем повторяющиеся задачи и реплики — их даты управляются правилами повторения
            .Where(t => t is { IsRecurring: false, RecurrenceSourceId: null })
            .ToList();

        // Сортировка по релевантности
        var sortedTasks = tasks
            .OrderBy(GetEffectivePriorityOrder)
            .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1) // Короткие первые
            .ThenByDescending(t => t.Interest ?? 0)
            .ToList();

        var today = TodoDay.Today;
        var tomorrow = today.AddDays(1);

        // Собираем все неактивные и фиксированные задачи для построения начальной загрузки по дням
        var allRootTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .ToList();

        // Словарь загрузки по дням
        var dailyStatsMap = new Dictionary<TodoDay, DailyStats>();

        DailyStats GetStatsForDay(TodoDay day)
        {
            if (dailyStatsMap.TryGetValue(day, out var existing))
                return existing;

            var stats = new DailyStats();
            foreach (var t in allRootTasks)
            {
                var isInactive = t.Status is TaskStatus.Completed or TaskStatus.Irrelevant;
                if (isInactive)
                {
                    var inactiveDate = t.CompletedDate ?? t.ScheduledDate;
                    if (inactiveDate.HasValue && day.IsSameDay(inactiveDate))
                    {
                        stats.TotalComplexity += t.TotalComplexity;
                        stats.TotalMinutes += t.TotalEstimatedMinutes;
                        stats.TaskCount++;
                    }
                }
                else if (t.Status != TaskStatus.NotConfigured)
                {
                    var isFixed = t.DateSource is DateSource.Manual or DateSource.AutoFixed;
                    if (isFixed && t.ScheduledDate.HasValue && day.IsSameDay(t.ScheduledDate))
                    {
                        stats.TotalComplexity += t.TotalComplexity;
                        stats.TotalMinutes += t.TotalEstimatedMinutes;
                        stats.TaskCount++;
                    }
                }
            }

            dailyStatsMap[day] = stats;
            return stats;
        }

        var currentDate = today;

        foreach (var task in sortedTasks)
        {
            while (true)
            {
                if (currentDate > tomorrow)
                {
                    taskRepository.UpdateTaskSchedule(task.Id, null, saveChanges: false);
                    break;
                }

                var dailyStats = GetStatsForDay(currentDate);

                if (CanAddToDay(task, dailyStats, settings))
                {
                    taskRepository.UpdateTaskSchedule(task.Id, currentDate.ToDateTime(), saveChanges: false);
                    Console.WriteLine($"Planner: assigned non-recurring task {task.Id} '{task.Title}' -> {currentDate}");

                    // Обновляем статистику дня (подзадачи не учитываются в счётчике задач)
                    dailyStats.TotalComplexity += task.TotalComplexity;
                    dailyStats.TotalMinutes += task.TotalEstimatedMinutes;
                    dailyStats.TaskCount++;
                    break;
                }

                currentDate = currentDate.AddDays(1);
            }
        }
    }

    /// <summary>
    /// Полный пересчёт
    /// </summary>
    public void RecalculateAll(UserSettings settings)
    {
        NormalizeTaskDateSources();
        ActualizePriorities();
        DistributeTasks(settings);
        UpdateBlockedStatuses();

        taskRepository.SaveChangesAndNotify();
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
            List<TaskItem?> blockers = [];

            blockers.AddRange(task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => r.SourceTask));

            var hasActiveBlockers = blockers.Any(b => b != null && b.Status != TaskStatus.Completed && b.Status != TaskStatus.Irrelevant);

            // Если есть активные блокеры, устанавливаем статус Blocked
            if (hasActiveBlockers && task.Status != TaskStatus.Blocked)
            {
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Blocked, saveChanges: false);
            }
            // Если нет активных блокеров, но статус Blocked, возвращаем к Planned
            else if (!hasActiveBlockers && task.Status == TaskStatus.Blocked)
            {
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Planned, saveChanges: false);
            }
        }
    }

    private int GetEffectivePriorityOrder(TaskItem task)
    {
        return task.Priority?.Order ?? 99;
    }

    private bool CanAddToDay(TaskItem task, DailyStats stats, UserSettings settings)
    {
        // Задача с критическим приоритетом (Order <= 1) проходит в обход лимитов
        if (GetEffectivePriorityOrder(task) <= 1)
        {
            return true;
        }

        // Если текущая загрузка дня УЖЕ исчерпала лимиты
        if (stats.TaskCount >= settings.DailyTaskLimit ||
            stats.TotalMinutes >= settings.DailyTimeLimit ||
            stats.TotalComplexity >= settings.DailyComplexityLimit)
        {
            return false;
        }

        // 1. Лимит количества задач (TaskCount) — СТРОГИЙ (без исключений)
        if (stats.TaskCount + 1 > settings.DailyTaskLimit)
        {
            return false;
        }

        // 2. Лимит времени — если превышает остаток времени дня, разрешено только для крупных по времени задач (>= 70% от dailyTimeLimit)
        if (stats.TotalMinutes + task.TotalEstimatedMinutes > settings.DailyTimeLimit)
        {
            var isLargeTimeTask = task.TotalEstimatedMinutes >= settings.DailyTimeLimit * AppConfig.LargeTaskThresholdPercent;
            if (!isLargeTimeTask)
            {
                return false;
            }
        }

        // 3. Лимит сложности — если превышает остаток сложности дня, разрешено только для крупных по сложности задач (>= 70% от dailyComplexityLimit)
        if (stats.TotalComplexity + task.TotalComplexity > settings.DailyComplexityLimit)
        {
            var isLargeComplexityTask = task.TotalComplexity >= settings.DailyComplexityLimit * AppConfig.LargeTaskThresholdPercent;
            if (!isLargeComplexityTask)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Обработать просроченные повторяющиеся задачи перед основным распределением.
    /// Scenario B из спецификации: мутируем задачу на месте — устанавливаем ScheduledDate = сегодня,
    /// DateSource = AutoFixed. Новый клон НЕ создаётся.
    /// </summary>
    private void HandleRecurringBeforeDistribution(UserSettings _)
    {
        taskRepository.NormalizeTaskDateSources();
    }

    private class DailyStats
    {
        public int TotalComplexity { get; set; }
        public int TotalMinutes { get; set; }
        public int TaskCount { get; set; }
    }
}
