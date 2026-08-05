using FlowFocus.Blazor.Components;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using MudBlazor;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Blazor.Helpers;

public static class TaskFilterEvaluator
{
    public static IEnumerable<TaskItem> ApplyDefaultFilter(IEnumerable<TaskItem> tasks, TaskListFilter? defaultFilter)
    {
        if (defaultFilter == null) return tasks;

        var today = TodoDay.Today;
        return defaultFilter.Type switch
        {
            TaskListFilterType.Today => tasks.Where(t =>
                today.IsSameDay(t.ScheduledDate) &&
                t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant),

            TaskListFilterType.Tomorrow => tasks.Where(t =>
                today.IsTomorrow(t.ScheduledDate) &&
                t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant),

            TaskListFilterType.NotConfigured => tasks.Where(t => t.Status == TaskStatus.NotConfigured),

            TaskListFilterType.Overdue => tasks.Where(t =>
                today.IsOverdue(t.ScheduledDate) &&
                t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant),

            _ => tasks
        };
    }

    public static IEnumerable<TaskItem> ApplySearchAndFilters(
        IEnumerable<TaskItem> tasks,
        string searchQuery,
        DateRange? dateRange,
        IEnumerable<TaskStatus?> selectedStatuses,
        DurationFilter durationFilter,
        IEnumerable<int?> selectedTagIds,
        bool hideWithDates)
    {
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            tasks = tasks.Where(t => t.Title.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (dateRange?.Start != null)
        {
            tasks = tasks.Where(t => t.ScheduledDate >= dateRange.Start);
        }
        if (dateRange?.End != null)
        {
            tasks = tasks.Where(t => t.ScheduledDate <= dateRange.End);
        }

        if (selectedStatuses.Any())
        {
            var statuses = selectedStatuses.Where(s => s != null).Cast<TaskStatus>().ToList();
            tasks = tasks.Where(t => statuses.Contains(t.Status));
        }

        tasks = durationFilter switch
        {
            DurationFilter.Short => tasks.Where(t => t.EstimatedMinutes <= AppConfig.ShortTaskThreshold),
            DurationFilter.Medium => tasks.Where(t => t.EstimatedMinutes is > AppConfig.ShortTaskThreshold and <= AppConfig.MediumTaskThreshold),
            DurationFilter.Long => tasks.Where(t => t.EstimatedMinutes is > AppConfig.MediumTaskThreshold and <= AppConfig.LongTaskThreshold),
            DurationFilter.MultiDay => tasks.Where(t => t.EstimatedMinutes > AppConfig.LongTaskThreshold),
            _ => tasks
        };

        if (selectedTagIds.Any())
        {
            var tagIds = selectedTagIds.Where(id => id != null).Cast<int>().ToList();
            tasks = tasks.Where(t => t.Tags.Any(tt => tagIds.Contains(tt.TagId)));
        }

        if (hideWithDates)
        {
            tasks = tasks.Where(t => t.DateSource is DateSource.AutoFlexible);
        }

        return tasks;
    }

    public static IEnumerable<TaskItem> ApplySort(IEnumerable<TaskItem> tasks, SortType sortType)
    {
        return sortType switch
        {
            SortType.Relevance => tasks
                .OrderBy(t => t.Status == TaskStatus.Planned ? 0 : 1)
                .ThenBy(t => t.Priority?.Order ?? 99)
                .ThenByDescending(t => t.Interest ?? 0)
                .ThenBy(t => t.EstimatedMinutes ?? 9999),

            SortType.DateAsc => tasks.OrderBy(t => t.ScheduledDate ?? DateTime.MaxValue),
            SortType.DateDesc => tasks.OrderByDescending(t => t.ScheduledDate ?? DateTime.MinValue),

            SortType.ComplexityAsc => tasks.OrderBy(t => t.Complexity ?? 0),
            SortType.ComplexityDesc => tasks.OrderByDescending(t => t.Complexity ?? 0),

            SortType.InterestAsc => tasks.OrderBy(t => t.Interest ?? 0),
            SortType.InterestDesc => tasks.OrderByDescending(t => t.Interest ?? 0),

            SortType.DurationAsc => tasks.OrderBy(t => t.EstimatedMinutes ?? 0),
            SortType.DurationDesc => tasks.OrderByDescending(t => t.EstimatedMinutes ?? 0),

            _ => tasks
        };
    }
}
