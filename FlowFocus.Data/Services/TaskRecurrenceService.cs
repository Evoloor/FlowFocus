using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

/// <summary>
/// Реализация сервиса управления и расчета повторяющихся задач
/// </summary>
public class TaskRecurrenceService : ITaskRecurrenceService
{
    public DateTime? CalculateNextRecurrenceDate(TaskItem task)
    {
        var completedDate = task.CompletedDate ?? TodoDay.Today.ToDateTime();
        var assignedDate = task.ScheduledDate ?? completedDate;
        var baseDate = completedDate.Date >= assignedDate.Date ? completedDate.Date : assignedDate.Date;

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

    public void HandleTaskCompletionRecurrence(TaskItem sourceTask, Func<int, DateTime?, DateTime?, bool> existsPredicate, Action<TaskItem> onNewTaskCreated)
    {
        try
        {
            var nextDate = CalculateNextRecurrenceDate(sourceTask);
            if (nextDate == null) return;

            var sourceId = sourceTask.RecurrenceSourceId ?? sourceTask.Id;
            var start = nextDate.Value.Date;
            var end = start.AddDays(1);

            var exists = existsPredicate(sourceId, start, end);
            if (exists) return;

            var newTask = CloneTaskItem(sourceTask, isParent: true, scheduledDate: nextDate, dateSource: DateSource.AutoFixed, recurrenceSourceId: sourceId);
            onNewTaskCreated(newTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HandleTaskCompletionRecurrence error for task {sourceTask?.Id}: {ex}");
        }
    }

    private TaskItem CloneTaskItem(
        TaskItem source, 
        bool isParent, 
        DateTime? scheduledDate = null, 
        DateSource? dateSource = null, 
        int? recurrenceSourceId = null) => new()
    {
        Title = source.Title,
        Description = source.Description,
        Status = TaskStatus.Planned,
        PriorityId = source.PriorityId,
        Interest = source.Interest,
        Complexity = source.Complexity,
        EstimatedMinutes = source.EstimatedMinutes,
        IsFavorite = source.IsFavorite,
        HideUnderSpoiler = source.HideUnderSpoiler,
        ScheduledDate = isParent ? (scheduledDate ?? source.ScheduledDate) : null,
        DateSource = isParent ? (dateSource ?? source.DateSource) : DateSource.AutoFlexible,
        IsRecurring = source.IsRecurring,
        RecurrenceType = source.RecurrenceType,
        RecurrenceInterval = source.RecurrenceInterval,
        RecurrenceWeekDays = source.RecurrenceWeekDays,
        RecurrenceSourceId = isParent ? (recurrenceSourceId ?? source.RecurrenceSourceId) : null,
        CreatedDate = DateTime.UtcNow,
        Tags = source.Tags?.Select(t => new TaskTag { TagId = t.TagId }).ToList() ?? [],
        PriorityEscalations = source.PriorityEscalations?
            .Select(e => new PriorityEscalation { TargetPriorityId = e.TargetPriorityId, EscalationDate = e.EscalationDate, IsApplied = e.IsApplied })
            .ToList() ?? [],
        Subtasks = source.Subtasks?.Select(s => CloneTaskItem(s, isParent: false)).ToList() ?? []
    };

    private static DateTime CalculateNextMonthDate(DateTime baseDate, int monthsInterval)
    {
        var interval = monthsInterval <= 0 ? 1 : monthsInterval;
        var target = baseDate.AddMonths(interval);
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        return new DateTime(target.Year, target.Month, Math.Min(baseDate.Day, daysInTarget));
    }

    private static DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        var interval = yearsInterval <= 0 ? 1 : yearsInterval;
        var targetYear = baseDate.Year + interval;
        var daysInTarget = DateTime.DaysInMonth(targetYear, baseDate.Month);
        return new DateTime(targetYear, baseDate.Month, Math.Min(baseDate.Day, daysInTarget));
    }

    private static DateTime CalculateNextWeekDayDate(DateTime baseDate, int weekDaysMask)
    {
        var currentDay = (int)baseDate.DayOfWeek;

        for (var i = 1; i <= 7; i++)
        {
            var nextDay = (currentDay + i) % 7;
            var nextMaskDay = nextDay == 0 ? 64 : 1 << (nextDay - 1);

            if ((weekDaysMask & nextMaskDay) != 0)
            {
                return baseDate.AddDays(i);
            }
        }

        return baseDate.AddDays(7);
    }
}
