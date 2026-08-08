using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

public class TaskDistributionPlanner(ITaskRepository taskRepository)
{
    public void DistributeTasks(UserSettings settings)
    {
        HandleRecurringBeforeDistribution();

        var allTasksMap = taskRepository.GetAll().ToDictionary(t => t.Id);

        var unassignedBlockers = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            .Where(t => !t.Conditions.Any(c => c.Condition != null && !c.Condition.IsActive))
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

        var tasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant && t.Status != TaskStatus.NotConfigured)
            .Where(t => t.DateSource == DateSource.AutoFlexible)
            .Where(t => !t.Conditions.Any(c => c.Condition != null && !c.Condition.IsActive))
            .Where(t => t is { IsRecurring: false, RecurrenceSourceId: null })
            .ToList();

        var sortedTasks = tasks
            .OrderBy(GetEffectivePriorityOrder)
            .ThenBy(t => t.TotalEstimatedMinutes <= AppConfig.ShortTaskThreshold ? 0 : 1)
            .ThenByDescending(t => t.Interest ?? 0)
            .ToList();

        var today = TodoDay.Today;
        var tomorrow = today.AddDays(1);

        var allRootTasks = taskRepository.GetAll()
            .Where(t => t.ParentTaskId == null)
            .ToList();

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

                    dailyStats.TotalComplexity += task.TotalComplexity;
                    dailyStats.TotalMinutes += task.TotalEstimatedMinutes;
                    dailyStats.TaskCount++;
                    break;
                }

                currentDate = currentDate.AddDays(1);
            }
        }
    }

    private void HandleRecurringBeforeDistribution()
    {
        taskRepository.NormalizeTaskDateSources();
    }

    private static int GetEffectivePriorityOrder(TaskItem task) => task.Priority?.Order ?? 99;

    private static bool CanAddToDay(TaskItem task, DailyStats stats, UserSettings settings)
    {
        if (GetEffectivePriorityOrder(task) <= 1)
        {
            return true;
        }

        if (stats.TaskCount >= settings.DailyTaskLimit ||
            stats.TotalMinutes >= settings.DailyTimeLimit ||
            stats.TotalComplexity >= settings.DailyComplexityLimit)
        {
            return false;
        }

        if (stats.TaskCount + 1 > settings.DailyTaskLimit)
        {
            return false;
        }

        if (stats.TotalMinutes + task.TotalEstimatedMinutes > settings.DailyTimeLimit)
        {
            var isLargeTimeTask = task.TotalEstimatedMinutes >= settings.DailyTimeLimit * AppConfig.LargeTaskThresholdPercent;
            if (!isLargeTimeTask)
            {
                return false;
            }
        }

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

    private class DailyStats
    {
        public int TotalComplexity { get; set; }
        public int TotalMinutes { get; set; }
        public int TaskCount { get; set; }
    }
}
