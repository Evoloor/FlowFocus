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
                saveChanges: false
            );
        }
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

        // Обработка приближающихся дедлайнов для фиксированных заблокированных задач:
        // Перевод блокеров в AutoFixed, если запас дней исчерпан.
        HandleApproachingDeadlinesForBlockedTasks(settings);

        // Задачи с AutoFlexible — кандидаты для авто-распределения
        var tasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null) // Исключаем подзадачи
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            // Только задачи, которые плановик может свободно перемещать
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            // Исключаем повторяющиеся задачи и реплики — их даты управляются правилами повторения
            .Where(t => t is { IsRecurring: false, RecurrenceSourceId: null })
            .ToList();

        // Сортировка по релевантности с учетом топологического порядка (блокирующие перед заблокированными)
        var sortedTasks = TopologicalSortTasks(tasks);

        var today = TodoDay.Today;
        var tomorrow = today.AddDays(1);

        // Собираем все задачи (Manual / AutoFixed), у которых уже зафиксирована дата
        var fixedTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .ToList();

        // Заполняем стартовую статистику на сегодня и на завтра на основе фиксированных задач
        DailyStats todayStats = new();
        DailyStats tomorrowStats = new();

        foreach (var task in fixedTasks)
        {
            if (today.IsSameDay(task.ScheduledDate))
            {
                todayStats.TotalComplexity += task.TotalComplexity;
                todayStats.TotalMinutes += task.TotalEstimatedMinutes;
                todayStats.TaskCount++;
            }
            else if (today.IsTomorrow(task.ScheduledDate))
            {
                tomorrowStats.TotalComplexity += task.TotalComplexity;
                tomorrowStats.TotalMinutes += task.TotalEstimatedMinutes;
                tomorrowStats.TaskCount++;
            }
        }

        var allTasks = taskRepository.GetAll();
        var allTasksById = allTasks.ToDictionary(t => t.Id);

        foreach (var task in sortedTasks)
        {
            // Находим активных блокеров текущей задачи через единый словарь в памяти
            var activeBlockers = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => allTasksById.GetValueOrDefault(r.SourceTaskId))
                .Where(b => b != null && b.Status != TaskStatus.Completed && b.Status != TaskStatus.Irrelevant)
                .ToList();

            // Определяем минимально допустимую дату для заблокированной задачи
            TodoDay minAllowedDay = today;
            bool cannotBeScheduled = false;

            if (activeBlockers.Count > 0)
            {
                // Если у какого-либо активного блокера дата назначена в null (не уместился до завтра),
                // то и заблокированную задачу нельзя назначить на сегодня/завтра
                if (activeBlockers.Any(b => b!.ScheduledDate == null))
                {
                    cannotBeScheduled = true;
                }
                else
                {
                    var maxBlockerDate = activeBlockers.Max(b => b!.ScheduledDate!.Value.Date);
                    var maxBlockerDay = new TodoDay(maxBlockerDate);
                    if (maxBlockerDay > minAllowedDay)
                    {
                        minAllowedDay = maxBlockerDay;
                    }
                }
            }

            if (cannotBeScheduled)
            {
                task.ScheduledDate = null;
                taskRepository.UpdateTaskSchedule(task.Id, null, saveChanges: false);
                Console.WriteLine($"Planner: task {task.Id} '{task.Title}' blocked by unassigned blocker, clearing ScheduledDate");
                continue;
            }

            var isLargeTask = IsLargeTask(task, settings);
            var currentDate = minAllowedDay;
            DailyStats dailyStats = currentDate == today ? todayStats : (currentDate == tomorrow ? tomorrowStats : new DailyStats());

            // Проверяем лимиты для текущего дня. Если превышен, пытаемся перейти на следующий день.
            while (!CanAddToDay(task, dailyStats, settings, isLargeTask))
            {
                currentDate = currentDate.AddDays(1);
                dailyStats = currentDate == tomorrow ? tomorrowStats : new DailyStats();
            }

            // Детальный просчёт только для "сегодня" и "завтра"
            if (currentDate <= tomorrow)
            {
                task.ScheduledDate = currentDate.ToDateTime();
                taskRepository.UpdateTaskSchedule(task.Id, task.ScheduledDate, saveChanges: false);
                Console.WriteLine($"Planner: assigned non-recurring task {task.Id} '{task.Title}' -> {currentDate}");
            }
            else
            {
                // Остальные задачи не имеют даты назначения
                task.ScheduledDate = null;
                taskRepository.UpdateTaskSchedule(task.Id, null, saveChanges: false);
                Console.WriteLine($"Planner: task {task.Id} '{task.Title}' beyond tomorrow, clearing ScheduledDate");
            }

            // Обновляем статистику дня (подзадачи не учитываются в счётчике задач)
            dailyStats.TotalComplexity += task.TotalComplexity;
            dailyStats.TotalMinutes += task.TotalEstimatedMinutes;
            dailyStats.TaskCount++;
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
    /// Обработать приближающиеся дедлайны для не-AutoFlexible заблокированных задач.
    /// Переводит блокеров в AutoFixed, если оставшегося времени/дней до дедлайна недостаточно.
    /// </summary>
    private void HandleApproachingDeadlinesForBlockedTasks(UserSettings settings)
    {
        var allTasks = taskRepository.GetAll();
        var today = TodoDay.Today.ToDateTime();

        var fixedBlockedTasks = allTasks
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .Where(t => t.InverseRelations.Any(r => r.Type == RelationType.Blocks && r.SourceTask != null && r.SourceTask.Status != TaskStatus.Completed && r.SourceTask.Status != TaskStatus.Irrelevant))
            .ToList();

        var dailyLimit = Math.Max(1, settings.DailyTimeLimit);

        foreach (var blockedTask in fixedBlockedTasks)
        {
            var targetDate = blockedTask.ScheduledDate!.Value.Date;

            // Собираем всех активных рекурсивных блокеров
            var blockers = GetActiveBlockersRecursive(blockedTask, allTasks);
            if (blockers.Count == 0) continue;

            int totalMinutes = blockedTask.TotalEstimatedMinutes + blockers.Sum(b => b.TotalEstimatedMinutes);
            int requiredDays = Math.Max(1, (int)Math.Ceiling((double)totalMinutes / dailyLimit));

            var earliestStartDate = targetDate.AddDays(-(requiredDays - 1));

            if (earliestStartDate <= today)
            {
                var assignedDate = targetDate < today ? today : targetDate;
                foreach (var blocker in blockers)
                {
                    if (blocker.DateSource == DateSource.AutoFlexible || !blocker.ScheduledDate.HasValue || blocker.ScheduledDate.Value.Date > targetDate)
                    {
                        blocker.ScheduledDate = assignedDate;
                        blocker.DateSource = DateSource.AutoFixed;
                        taskRepository.UpdateTaskSchedule(blocker.Id, assignedDate, DateSource.AutoFixed, saveChanges: false);
                    }
                }
            }
        }
    }

    private List<TaskItem> GetActiveBlockersRecursive(TaskItem task, List<TaskItem> allTasks)
    {
        var visited = new HashSet<int>();
        var result = new List<TaskItem>();
        var queue = new Queue<TaskItem>();
        queue.Enqueue(task);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var activeBlockerRelations = current.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .ToList();

            foreach (var rel in activeBlockerRelations)
            {
                var blocker = allTasks.FirstOrDefault(t => t.Id == rel.SourceTaskId);
                if (blocker != null && blocker.Status != TaskStatus.Completed && blocker.Status != TaskStatus.Irrelevant)
                {
                    if (visited.Add(blocker.Id))
                    {
                        result.Add(blocker);
                        queue.Enqueue(blocker);
                    }
                }
            }
        }

        return result;
    }

    private List<TaskItem> TopologicalSortTasks(List<TaskItem> tasks)
    {
        var taskIds = tasks.Select(t => t.Id).ToHashSet();
        var inDegree = tasks.ToDictionary(t => t.Id, _ => 0);
        var dependents = tasks.ToDictionary(t => t.Id, _ => new List<int>());

        foreach (var task in tasks)
        {
            var blockerIds = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks && r.SourceTask != null && r.SourceTask.Status != TaskStatus.Completed && r.SourceTask.Status != TaskStatus.Irrelevant)
                .Select(r => r.SourceTaskId)
                .Where(id => taskIds.Contains(id))
                .Distinct();

            foreach (var blockerId in blockerIds)
            {
                inDegree[task.Id]++;
                if (!dependents.ContainsKey(blockerId))
                    dependents[blockerId] = [];
                dependents[blockerId].Add(task.Id);
            }
        }

        var result = new List<TaskItem>();
        var available = tasks.Where(t => inDegree[t.Id] == 0).ToList();

        while (available.Count > 0)
        {
            var best = available
                .OrderBy(GetEffectivePriorityOrder)
                .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1)
                .ThenByDescending(t => t.Interest ?? 0)
                .First();

            available.Remove(best);
            result.Add(best);

            if (dependents.TryGetValue(best.Id, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    inDegree[childId]--;
                    if (inDegree[childId] == 0)
                    {
                        var childTask = tasks.First(t => t.Id == childId);
                        available.Add(childTask);
                    }
                }
            }
        }

        if (result.Count < tasks.Count)
        {
            var remaining = tasks.Where(t => !result.Contains(t))
                .OrderBy(GetEffectivePriorityOrder)
                .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1)
                .ThenByDescending(t => t.Interest ?? 0);
            result.AddRange(remaining);
        }

        return result;
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
