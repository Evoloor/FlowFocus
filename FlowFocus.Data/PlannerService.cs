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
                escalations.Select(e => e.Id)
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
        taskRepository.NormalizeTaskDateSources();
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

        var currentDate = today;
        DailyStats dailyStats = todayStats;

        foreach (var task in sortedTasks)
        {
            var isLargeTask = IsLargeTask(task, settings);

            // Проверяем лимиты для текущего дня. Если превышен, пытаемся перейти на следующий день.
            while (!CanAddToDay(task, dailyStats, settings, isLargeTask))
            {
                currentDate = currentDate.AddDays(1);
                dailyStats = currentDate == tomorrow ? tomorrowStats : new DailyStats();
            }

            // Детальный просчёт только для "сегодня" и "завтра"
            if (currentDate <= tomorrow)
            {
                taskRepository.UpdateTaskSchedule(task.Id, currentDate.ToDateTime());
                Console.WriteLine($"Planner: assigned non-recurring task {task.Id} '{task.Title}' -> {currentDate}");
            }
            else
            {
                // Остальные задачи не имеют даты назначения
                taskRepository.UpdateTaskSchedule(task.Id, null);
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
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Blocked);
            }
            // Если нет активных блокеров, но статус Blocked, возвращаем к Planned
            else if (!hasActiveBlockers && task.Status == TaskStatus.Blocked)
            {
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Planned);
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
        var today = TodoDay.Today;
        var recurringTasks = taskRepository.GetRecurringCandidatesForPlanner();

        Console.WriteLine($"Planner: found {recurringTasks.Count} recurring candidates for handling");
        foreach (var c in recurringTasks)
        {
            Console.WriteLine($"Planner: candidate {c.Id} '{c.Title}' RecType={c.RecurrenceType} IsRecurring={c.IsRecurring} Scheduled={c.ScheduledDate?.ToString("yyyy-MM-dd") ?? "<null>"} DateSource={c.DateSource}");
        }

        foreach (var task in recurringTasks)
        {
            // Вычисляем следующую дату по правилу повторения от последней известной базы
            DateTime baseCandidate;
            if (task.CompletedDate.HasValue)
            {
                // Если задача была завершена, используем дату завершения как базу
                var completedDate = task.CompletedDate.Value;
                var assignedDate = task.ScheduledDate ?? completedDate;
                baseCandidate = completedDate.Date >= assignedDate.Date ? completedDate.Date : assignedDate.Date;
            }
            else
            {
                // Незавершённая задача: берём последнюю назначенную дату как базу
                baseCandidate = task.ScheduledDate?.Date ?? TodoDay.Today.Yesterday.ToDateTime();
            }

            // Продвигаем по правилу повтора, пока не дойдём до даты >= logicalToday
            var nextDate = GetNextRecurrenceDateFromBase(task, baseCandidate);
            if (nextDate == null)
                continue;

            var guard = 0;
            while (nextDate.HasValue && nextDate.Value.Date < today.Date && guard < 365)
            {
                baseCandidate = nextDate.Value.Date;
                nextDate = GetNextRecurrenceDateFromBase(task, baseCandidate);
                guard++;
            }

            // Scenario B: мутируем существующую задачу на месте
            // Если следующая дата в будущем — используем её; иначе — сегодня
            var assigned = nextDate.HasValue && nextDate.Value.Date >= today.Date
                ? nextDate.Value.Date
                : today.ToDateTime();

            taskRepository.MutateRecurringTaskInPlace(task.Id, assigned);
            Console.WriteLine($"Planner: recurring task {task.Id} '{task.Title}' mutated in-place -> Scheduled={assigned:yyyy-MM-dd} DateSource=AutoFixed");
        }
    }

    // Рассчитывает следующую дату повтора, считая от baseDate
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
        return new(target.Year, target.Month, chosenDay);
    }

    private DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        if (yearsInterval <= 0) yearsInterval = 1;
        var targetYear = baseDate.Year + yearsInterval;
        var month = baseDate.Month;
        var day = baseDate.Day;

        var daysInTarget = DateTime.DaysInMonth(targetYear, month);
        var chosenDay = day > daysInTarget ? daysInTarget : day;
        return new(targetYear, month, chosenDay);
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
