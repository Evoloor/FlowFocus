using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

/// <summary>
/// Реализация сервиса управления и расчета повторяющихся задач
/// </summary>
public class TaskRecurrenceService : ITaskRecurrenceService
{
    public DateTime? CalculateNextRecurrenceDate(TaskItem task)
    {
        // Расчёт следующей даты повторения всегда выполняется от фактической даты завершения (или от сегодня).
        // Досрочное выполнение задач с будущей датой назначения также отсчитывает следующий повтор от сегодня.
        var completedDate = task.CompletedDate ?? TodoDay.Today.ToDateTime();
        return CalculateNextRecurrenceDateFromBase(task, completedDate.Date);
    }

    public DateTime? CalculateNextRecurrenceDateFromBase(TaskItem task, DateTime baseDate)
    {
        return task.RecurrenceType switch
        {
            RecurrenceType.EveryN => task.RecurrenceUnit switch
            {
                RecurrenceUnit.Days => baseDate.AddDays(task.RecurrenceInterval ?? 1),
                RecurrenceUnit.Months => CalculateNextMonthDate(baseDate, task.RecurrenceInterval ?? 1),
                RecurrenceUnit.Years => CalculateNextYearDate(baseDate, task.RecurrenceInterval ?? 1),
                _ => baseDate.AddDays(task.RecurrenceInterval ?? 1)
            },
            RecurrenceType.WeekDays => CalculateNextWeekDayDate(baseDate, task.RecurrenceWeekDays ?? 0),
            _ => null
        };
    }

    public void HandleTaskCompletionRecurrence(TaskItem sourceTask, Func<int, DateTime?, DateTime?, bool> existsPredicate, Action<TaskItem> onNewTaskCreated)
    {
        try
        {
            var nextDate = CalculateNextRecurrenceDate(sourceTask);
            if (nextDate == null) return;

            var todayDt = TodoDay.Today.ToDateTime();
            while (nextDate != null && nextDate.Value.Date <= todayDt.Date)
            {
                nextDate = CalculateNextRecurrenceDateFromBase(sourceTask, nextDate.Value.Date);
            }

            if (nextDate == null) return;

            var sourceId = sourceTask.RecurrenceSourceId ?? sourceTask.Id;
            var start = nextDate.Value.Date;
            var end = start.AddDays(1);

            var exists = existsPredicate(sourceId, start, end);
            if (exists) return;

            var newTask = CloneTaskItem(sourceTask, scheduledDate: nextDate, dateSource: DateSource.AutoFixed, recurrenceSourceId: sourceId);
            onNewTaskCreated(newTask);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"HandleTaskCompletionRecurrence error for task {sourceTask?.Id}: {ex}");
            throw;
        }
    }

    private static TaskItem CloneTaskItem(
        TaskItem source,
        DateTime? scheduledDate = null, 
        DateSource? dateSource = null, 
        int? recurrenceSourceId = null)
    {
        var newTask = new TaskItem
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
            ScheduledDate = scheduledDate ?? source.ScheduledDate,
            DateSource = dateSource ?? source.DateSource,
            IsRecurring = source.IsRecurring,
            RecurrenceType = source.RecurrenceType,
            RecurrenceUnit = source.RecurrenceUnit,
            RecurrenceInterval = source.RecurrenceInterval,
            RecurrenceWeekDays = source.RecurrenceWeekDays,
            RecurrenceSourceId = recurrenceSourceId ?? source.RecurrenceSourceId,
            CreatedDate = DateTime.UtcNow,
            Tags = source.Tags?.Select(t => new TaskTag { TagId = t.TagId }).ToList() ?? [],
            Conditions = source.Conditions?.Select(c => new TaskCondition { ConditionId = c.ConditionId }).ToList() ?? [],
            PriorityEscalations = source.PriorityEscalations?
                .Select(e => new PriorityEscalation { TargetPriorityId = e.TargetPriorityId, EscalationDate = e.EscalationDate, IsApplied = e.IsApplied })
                .ToList() ?? []
        };

        if (source.Subtasks is { Count: > 0 })
        {
            newTask.Subtasks = source.Subtasks.Select((s, i) => new SubtaskItem
            {
                Title = s.Title,
                Description = s.Description,
                HideUnderSpoiler = s.HideUnderSpoiler,
                IsFavorite = s.IsFavorite,
                Interest = s.Interest,
                Complexity = s.Complexity,
                EstimatedMinutes = s.EstimatedMinutes,
                Status = TaskStatus.Planned,
                CreatedDate = DateTime.UtcNow,
                ParentTask = newTask,
                SortOrder = i
            }).ToList();
        }

        return newTask;
    }

    private static DateTime CalculateNextMonthDate(DateTime baseDate, int monthsInterval)
    {
        var interval = monthsInterval <= 0 ? 1 : monthsInterval;
        var target = baseDate.AddMonths(interval);
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        return new(target.Year, target.Month, Math.Min(baseDate.Day, daysInTarget));
    }

    private static DateTime CalculateNextYearDate(DateTime baseDate, int yearsInterval)
    {
        var interval = yearsInterval <= 0 ? 1 : yearsInterval;
        var targetYear = baseDate.Year + interval;
        var daysInTarget = DateTime.DaysInMonth(targetYear, baseDate.Month);
        return new(targetYear, baseDate.Month, Math.Min(baseDate.Day, daysInTarget));
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
