using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data;

/// <summary>
/// Сервис алгоритмического планирования задач
/// </summary>
public class PlannerService(
    ITaskRepository taskRepository,
    IPriorityRepository priorityRepository,
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

        context.SaveChanges();
    }

    /// <summary>
    /// Шаг 2: Распределение задач по дням
    /// </summary>
    public void DistributeTasks(UserSettings settings)
    {
        var tasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null) // Исключаем подзадачи
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.UserAssignedDate == null) // Только задачи без ручной даты
            .ToList();

        // Сортировка по релевантности
        var sortedTasks = tasks
            .OrderBy(t => GetEffectivePriorityOrder(t))
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
            .ToList();

        foreach (var task in fixedDateTasks)
        {
            var trackedTask = context.Tasks.Find(task.Id);
            if (trackedTask != null)
            {
                trackedTask.ActualAssignedDate = task.UserAssignedDate;
                trackedTask.LastChangesOn = DateTime.UtcNow;
            }
        }

        context.SaveChanges();
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

            // Собираем всех активных блокеров как из явных связей BlockedBy, так и из обратных связей Blocks
            var blockers = new List<TaskItem?>();

            blockers.AddRange(task.Relations
                .Where(r => r.Type == RelationType.BlockedBy)
                .Select(r => r.TargetTask));

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

        context.SaveChanges();
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

    private class DailyStats
    {
        public int TotalComplexity { get; set; }
        public int TotalMinutes { get; set; }
        public int TaskCount { get; set; }
    }
}

