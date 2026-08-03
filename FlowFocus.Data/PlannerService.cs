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
    /// 1. Нормализация приоритетов блокеров:
    /// Если задача B заблокирована задачей A, и приоритет A ниже, чем у B (Order у A больше) — повысить приоритет задачи A до уровня задачи B.
    /// Выполнять для всех активных задач (кроме Completed и Irrelevant).
    /// </summary>
    public void NormalizeBlockerPriorities()
    {
        var activeTasks = taskRepository.GetAll()
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant)
            .ToList();

        var taskMap = activeTasks.ToDictionary(t => t.Id);

        bool changed;
        do
        {
            changed = false;
            foreach (var taskB in activeTasks)
            {
                if (taskB.Priority == null) continue;

                var blockerIds = taskB.InverseRelations
                    .Where(r => r.Type == RelationType.Blocks)
                    .Select(r => r.SourceTaskId)
                    .Concat(activeTasks.Where(other => other.Relations.Any(r => r.Type == RelationType.Blocks && r.TargetTaskId == taskB.Id)).Select(other => other.Id))
                    .Distinct();

                foreach (var blockerId in blockerIds)
                {
                    if (blockerId == 0 || !taskMap.TryGetValue(blockerId, out var taskA)) continue;
                    if (taskA.Status is TaskStatus.Completed or TaskStatus.Irrelevant) continue;

                    var orderA = taskA.Priority?.Order ?? 99;
                    var orderB = taskB.Priority.Order;

                    if (orderA > orderB)
                    {
                        taskRepository.UpdateTaskPriority(taskA.Id, taskB.PriorityId!.Value, saveChanges: false);
                        taskA.PriorityId = taskB.PriorityId;
                        typeof(TaskItem).GetProperty(nameof(TaskItem.Priority))?.SetValue(taskA, taskB.Priority);
                        changed = true;
                    }
                }
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
    /// Перераспределяются задачи с DateSource == AutoFlexible.
    /// Задачи с Manual или AutoFixed остаются на своих датах.
    /// </summary>
    public void DistributeTasks(UserSettings settings)
    {
        // Сначала обрабатываем повторяющиеся задачи: если они просрочены/неназначены,
        // мутируем их на месте (Scenario B из спецификации).
        HandleRecurringBeforeDistribution(settings);

        var allActiveTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .ToList();

        var today = TodoDay.Today;

        // 2. Deficit Protection & Точечный AutoFixed для блокеров:
        // Проверяем все фиксированные (Manual / AutoFixed) заблокированные задачи.
        // Оцениваем, сколько дней доступно до их даты, и хватает ли суммарной дневной емкости для их блокеров.
        // Если емкости НЕ хватает (дефицит), переводим МИНИМАЛЬНО необходимое количество блокеров в AutoFixed на дату этой задачи.
        var fixedBlockedTasks = allActiveTasks
            .Where(t => (t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed) && t.ScheduledDate.HasValue)
            .OrderBy(t => t.ScheduledDate!.Value)
            .ToList();

        foreach (var taskB in fixedBlockedTasks)
        {
            var blockers = GetActiveBlockersRecursive(taskB, allActiveTasks);
            if (blockers.Count == 0) continue;

            var targetDate = taskB.ScheduledDate!.Value.Date;
            var daysCount = Math.Max(1, (targetDate - today.ToDateTime().Date).Days);
            var totalCapacityMinutes = daysCount * settings.DailyTimeLimit;

            // Всего требуется времени на цепочку (блокеры + сама заблокированная задача B)
            var totalChainMinutes = blockers.Sum(b => b.TotalEstimatedMinutes) + taskB.TotalEstimatedMinutes;

            if (totalChainMinutes > totalCapacityMinutes)
            {
                var uncommittedBlockers = blockers
                    .Where(b => b.DateSource == DateSource.AutoFlexible)
                    .OrderBy(GetEffectivePriorityOrder)
                    .ThenByDescending(b => b.TotalEstimatedMinutes)
                    .ToList();

                var currentDeficit = totalChainMinutes - totalCapacityMinutes;
                foreach (var blocker in uncommittedBlockers)
                {
                    if (currentDeficit <= 0) break;

                    taskRepository.UpdateTaskSchedule(blocker.Id, targetDate, DateSource.AutoFixed, saveChanges: false);
                    blocker.ScheduledDate = targetDate;
                    blocker.DateSource = DateSource.AutoFixed;

                    currentDeficit -= blocker.TotalEstimatedMinutes;
                }
            }
        }

        // Кандидаты для авто-распределения (AutoFlexible) - переспрашиваем репозиторий, так как дефицит мог перевести задачи в AutoFixed
        var tasksToSchedule = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            .Where(t => t is { IsRecurring: false, RecurrenceSourceId: null })
            .ToList();

        // Топологическая сортировка + сортировка по приоритету:
        // Блокирующая задача A должна планироваться раньше или в тот же день, что и B.
        var sortedTasks = TopologicalAndPrioritySort(tasksToSchedule);

        var tomorrow = today.AddDays(1);

        // Собираем фиксированные задачи
        var fixedTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .ToList();

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

        var dayStatsMap = new Dictionary<DateTime, DailyStats>
        {
            [today.ToDateTime().Date] = todayStats,
            [tomorrow.ToDateTime().Date] = tomorrowStats
        };

        // Отслеживаем уже назначенную дату для блокеров каждой задачи
        var assignedDates = fixedTasks.ToDictionary(t => t.Id, t => t.ScheduledDate!.Value.Date);

        foreach (var task in sortedTasks)
        {
            // Проверяем минимальную допустимую дату для task (не раньше, чем даты всех его блокеров)
            var activeBlockerIds = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks && r.SourceTaskId != 0)
                .Select(r => r.SourceTaskId)
                .Concat(allActiveTasks.Where(other => other.Relations.Any(r => r.Type == RelationType.Blocks && r.TargetTaskId == task.Id)).Select(other => other.Id))
                .Distinct();

            var minDate = today.ToDateTime().Date;
            foreach (var blockerId in activeBlockerIds)
            {
                if (assignedDates.TryGetValue(blockerId, out var blockerDate))
                {
                    if (blockerDate > minDate)
                    {
                        minDate = blockerDate;
                    }
                }
            }

            var currentDate = today;
            if (currentDate.ToDateTime().Date < minDate)
            {
                currentDate = new TodoDay(minDate);
            }

            if (!dayStatsMap.TryGetValue(currentDate.ToDateTime().Date, out var dailyStats))
            {
                dailyStats = new DailyStats();
                dayStatsMap[currentDate.ToDateTime().Date] = dailyStats;
            }

            var isLargeTask = IsLargeTask(task, settings);

            while (!CanAddToDay(task, dailyStats, settings, isLargeTask))
            {
                currentDate = currentDate.AddDays(1);
                var dtKey = currentDate.ToDateTime().Date;
                if (!dayStatsMap.TryGetValue(dtKey, out dailyStats))
                {
                    dailyStats = new DailyStats();
                    dayStatsMap[dtKey] = dailyStats;
                }
            }

            var assignedDt = currentDate.ToDateTime();
            taskRepository.UpdateTaskSchedule(task.Id, assignedDt, saveChanges: false);
            task.ScheduledDate = assignedDt;
            assignedDates[task.Id] = assignedDt.Date;
            Console.WriteLine($"Planner: assigned non-recurring task {task.Id} '{task.Title}' -> {currentDate}");

            dailyStats.TotalComplexity += task.TotalComplexity;
            dailyStats.TotalMinutes += task.TotalEstimatedMinutes;
            dailyStats.TaskCount++;
        }
    }

    private List<TaskItem> GetActiveBlockersRecursive(TaskItem task, List<TaskItem> allTasks)
    {
        var taskMap = allTasks.ToDictionary(t => t.Id);
        var blockers = new List<TaskItem>();
        var visited = new HashSet<int>();

        void Traverse(TaskItem t)
        {
            var blockerIds = t.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => r.SourceTaskId)
                .Concat(allTasks.Where(other => other.Relations.Any(r => r.Type == RelationType.Blocks && r.TargetTaskId == t.Id)).Select(other => other.Id))
                .Distinct();

            foreach (var blockerId in blockerIds)
            {
                if (blockerId == 0 || !taskMap.TryGetValue(blockerId, out var blocker)) continue;
                if (blocker.Status is TaskStatus.Completed or TaskStatus.Irrelevant) continue;

                if (visited.Add(blocker.Id))
                {
                    blockers.Add(blocker);
                    Traverse(blocker);
                }
            }
        }

        Traverse(task);
        return blockers;
    }

    private List<TaskItem> TopologicalAndPrioritySort(List<TaskItem> tasks)
    {
        var taskMap = tasks.ToDictionary(t => t.Id);
        var inDegree = tasks.ToDictionary(t => t.Id, t => 0);
        var adj = tasks.ToDictionary(t => t.Id, t => new List<int>());

        foreach (var task in tasks)
        {
            var blockerIds = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks && r.SourceTaskId != 0 && taskMap.ContainsKey(r.SourceTaskId))
                .Select(r => r.SourceTaskId)
                .Concat(tasks.Where(other => other.Relations.Any(r => r.Type == RelationType.Blocks && r.TargetTaskId == task.Id)).Select(other => other.Id))
                .Distinct();

            foreach (var blockerId in blockerIds)
            {
                if (taskMap.ContainsKey(blockerId))
                {
                    adj[blockerId].Add(task.Id);
                    inDegree[task.Id]++;
                }
            }
        }

        var sorted = new List<TaskItem>();
        var readySet = tasks.Where(t => inDegree[t.Id] == 0).ToList();

        while (readySet.Count > 0)
        {
            readySet = readySet
                .OrderBy(GetEffectivePriorityOrder)
                .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1)
                .ThenByDescending(t => t.Interest ?? 0)
                .ToList();

            var current = readySet[0];
            readySet.RemoveAt(0);
            sorted.Add(current);

            foreach (var neighborId in adj[current.Id])
            {
                inDegree[neighborId]--;
                if (inDegree[neighborId] == 0)
                {
                    readySet.Add(taskMap[neighborId]);
                }
            }
        }

        // Защита на случай циклов (если какие-то задачи остались вне sorted)
        if (sorted.Count < tasks.Count)
        {
            var remaining = tasks.Where(t => !sorted.Contains(t))
                .OrderBy(GetEffectivePriorityOrder)
                .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1)
                .ThenByDescending(t => t.Interest ?? 0);
            sorted.AddRange(remaining);
        }

        return sorted;
    }

    /// <summary>
    /// Полный пересчёт
    /// </summary>
    public void RecalculateAll(UserSettings settings)
    {
        NormalizeTaskDateSources();
        NormalizeBlockerPriorities();
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
