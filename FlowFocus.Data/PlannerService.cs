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

        var allTasks = taskRepository.GetAll();
        var allTasksById = allTasks.ToDictionary(t => t.Id);

        // Перевод блокеров в AutoFixed выполняется СТРОГО при дефиците доступного времени до мануальной даты заблокированной задачи
        ConvertDeficitBlockersToAutoFixed(allTasks, allTasksById, settings);

        // Задачи с AutoFlexible — кандидаты для авто-распределения
        var tasks = allTasks
            .Where(t => t.ParentTaskId == null) // Исключаем подзадачи
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            // Только задачи, которые плановик может свободно перемещать
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            // Исключаем повторяющиеся задачи и реплики — их даты управляются правилами повторения
            .Where(t => t is { IsRecurring: false, RecurrenceSourceId: null })
            .ToList();

        var candidateTasksById = tasks.ToDictionary(t => t.Id);

        // Сортировка по релевантности с учетом топологического порядка (блокирующие перед заблокированными)
        var sortedTasks = TopologicalSortTasks(tasks, candidateTasksById);

        var today = TodoDay.Today;
        var tomorrow = today.AddDays(1);

        // Собираем все задачи (Manual / AutoFixed), у которых уже зафиксирована дата
        var fixedTasks = allTasks
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .ToList();

        // Карта вируальных рассчитанных дней плановика (для предотвращения ложного сброса в null при выходе блокера за tomorrow)
        var calculatedDayByTaskId = new Dictionary<int, TodoDay>();
        foreach (var ft in fixedTasks)
        {
            calculatedDayByTaskId[ft.Id] = new TodoDay(ft.ScheduledDate!.Value);
        }

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

        foreach (var task in sortedTasks)
        {
            // Находим активных блокеров текущей задачи через единый словарь в памяти
            var activeBlockers = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => allTasksById.GetValueOrDefault(r.SourceTaskId))
                .Where(b => b != null && b.Status != TaskStatus.Completed && b.Status != TaskStatus.Irrelevant)
                .ToList();

            // Задачи без блокировок не участвуют в ограничении даты
            TodoDay minAllowedDay = today;
            bool cannotBeScheduled = false;

            if (activeBlockers.Count > 0)
            {
                // Проверяем, был ли каждый активный блокер рассчитан/назначен на какой-либо день (даже выходящий за пределы tomorrow).
                // Если блокер не был рассчитан вовсе, то заблокированная задача не может быть назначена.
                if (activeBlockers.Any(b => !calculatedDayByTaskId.ContainsKey(b!.Id)))
                {
                    cannotBeScheduled = true;
                }
                else
                {
                    var maxBlockerDay = activeBlockers.Max(b => calculatedDayByTaskId[b!.Id]);
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

            // Фиксируем рассчитанный виртуальный день плановика
            calculatedDayByTaskId[task.Id] = currentDate;

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
    /// Переводит блокеров в AutoFixed СТРОГО в случае дефицита доступной емкости до даты заблокированной задачи.
    /// Если блокеры свободно помещаются в лимиты до этой даты, они остаются AutoFlexible.
    /// </summary>
    private void ConvertDeficitBlockersToAutoFixed(List<TaskItem> allTasks, Dictionary<int, TaskItem> allTasksById, UserSettings settings)
    {
        var today = TodoDay.Today.ToDateTime();

        var fixedBlockedTasks = allTasks
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .Where(t => t.InverseRelations.Any(r => r.Type == RelationType.Blocks))
            .ToList();

        if (fixedBlockedTasks.Count == 0) return;

        var dailyLimit = Math.Max(1, settings.DailyTimeLimit);

        // Фиксированные задачи для расчёта задействованной емкости
        var fixedTasks = allTasks
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.Manual || t.DateSource == DateSource.AutoFixed)
            .Where(t => t.ScheduledDate.HasValue)
            .ToList();

        var fixedMinutesPerDay = fixedTasks
            .GroupBy(t => t.ScheduledDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.TotalEstimatedMinutes));

        foreach (var blockedTask in fixedBlockedTasks)
        {
            var targetDate = blockedTask.ScheduledDate!.Value.Date;
            if (targetDate < today) targetDate = today;

            var autoFlexibleBlockers = GetActiveBlockersRecursive(blockedTask, allTasksById)
                .Where(b => b.DateSource == DateSource.AutoFlexible)
                .ToList();

            if (autoFlexibleBlockers.Count == 0) continue;

            // Расчет доступной емкости до targetDate включительно
            int availableCapacity = 0;
            for (var d = today.Date; d <= targetDate; d = d.AddDays(1))
            {
                int used = fixedMinutesPerDay.GetValueOrDefault(d, 0);
                availableCapacity += Math.Max(0, dailyLimit - used);
            }

            int totalBlockerMinutes = autoFlexibleBlockers.Sum(b => b.TotalEstimatedMinutes);
            int deficit = totalBlockerMinutes - availableCapacity;

            // Если дефицита нет, задачи остаются AutoFlexible и планируются в обычном потоке
            if (deficit <= 0) continue;

            int convertedMinutes = 0;
            foreach (var blocker in autoFlexibleBlockers)
            {
                blocker.ScheduledDate = targetDate;
                blocker.DateSource = DateSource.AutoFixed;
                taskRepository.UpdateTaskSchedule(blocker.Id, targetDate, DateSource.AutoFixed, saveChanges: false);

                // Обновляем карту фиксированных минут для targetDate, чтобы последующие фиксированные задачи учитывали эту нагрузку
                fixedMinutesPerDay[targetDate] = fixedMinutesPerDay.GetValueOrDefault(targetDate, 0) + blocker.TotalEstimatedMinutes;

                convertedMinutes += Math.Max(1, blocker.TotalEstimatedMinutes);
                if (convertedMinutes >= deficit)
                    break;
            }
        }
    }

    private List<TaskItem> GetActiveBlockersRecursive(TaskItem task, Dictionary<int, TaskItem> allTasksById)
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
                if (allTasksById.TryGetValue(rel.SourceTaskId, out var blocker))
                {
                    if (blocker.Status != TaskStatus.Completed && blocker.Status != TaskStatus.Irrelevant)
                    {
                        if (visited.Add(blocker.Id))
                        {
                            result.Add(blocker);
                            queue.Enqueue(blocker);
                        }
                    }
                }
            }
        }

        return result;
    }

    private List<TaskItem> TopologicalSortTasks(List<TaskItem> tasks, Dictionary<int, TaskItem> candidateTasksById)
    {
        var candidateIds = tasks.Select(t => t.Id).ToHashSet();
        var inDegree = tasks.ToDictionary(t => t.Id, _ => 0);
        var dependents = tasks.ToDictionary(t => t.Id, _ => new List<int>());

        foreach (var task in tasks)
        {
            var blockerIds = task.InverseRelations
                .Where(r => r.Type == RelationType.Blocks)
                .Select(r => r.SourceTaskId)
                .Where(id => candidateIds.Contains(id))
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
                    if (inDegree[childId] == 0 && candidateTasksById.TryGetValue(childId, out var childTask))
                    {
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
