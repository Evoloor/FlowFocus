using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Repositories.Helpers;

/// <summary>
/// Методы-расширения для специализированной фильтрации и выбора задач
/// </summary>
public static class TaskQueryExtensions
{
    public static IEnumerable<TaskItem> FilterActiveRootTasks(this IEnumerable<TaskItem> tasks) =>
        tasks.Where(t => t.ParentTaskId == null && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant);

    public static TaskItem? FindProcrastinationTask(this IEnumerable<TaskItem> tasks, List<int> excludeIds) =>
        tasks
            .Where(t => t.ParentTaskId == null &&
                        t is { Interest: >= AppConfig.MinProcrastinationInterest, Status: TaskStatus.Planned } &&
                        !excludeIds.Contains(t.Id))
            .OrderByDescending(t => t.Interest - Math.Sqrt(t.Priority?.Order ?? 99))
            .FirstOrDefault();

    public static TaskItem? FindLeastPriorityTaskOfDay(this IEnumerable<TaskItem> tasks)
    {
        var today = TodoDay.Today;
        return tasks
            .FilterActiveRootTasks()
            .Where(t => t.ScheduledDate != null && today.IsSameDay(t.ScheduledDate) && t.Status == TaskStatus.Planned)
            .OrderByDescending(t => t.Priority?.Order ?? 99)
            .ThenBy(t => t.Interest ?? 0)
            .FirstOrDefault();
    }

    public static List<TaskItem> FindRecurringCandidatesForPlanner(this StorageContext context)
    {
        var today = TodoDay.Today;

        return context.Tasks
            .AsNoTracking()
            .Where(t => t.ParentTaskId == null &&
                        t.Status != TaskStatus.Completed &&
                        t.Status != TaskStatus.Irrelevant &&
                        t.Status != TaskStatus.NotConfigured &&
                        t.IsRecurring)
            .ToList()
            .Where(t => (t.DateSource != DateSource.Manual || today.IsOverdue(t.ScheduledDate)) &&
                        (today.IsOverdue(t.ScheduledDate) || t.ScheduledDate == null))
            .ToList();
    }
}
