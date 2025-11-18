using System.ComponentModel.DataAnnotations;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

public interface IAuditEntity
{
    int Id { get; set; }
    DateTime LastChangesOn { get; set; }
}

public class TaskItem : IAuditEntity
{
    public TaskItem()
    {
    }

    public TaskItem(TaskItem task)
    {
        Title = task.Title;
        Description = task.Description;
        UserPriority = task.UserPriority;
        Status = TaskStatus.Planned;
        Interest = task.Interest;
        Complexity = task.Complexity;
        EstimatedHours = task.EstimatedHours;
        PlannedDate = task.PlannedDate;
        IsRecurring = true;
        Recurrence = task.Recurrence;
        RecurrenceEndDate = task.RecurrenceEndDate;
        ParentTaskId = task.Id;
        Tags = [..task.Tags];
        DisplayType = task.DisplayType;
    }

    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    [Required] [MaxLength(500)] public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public TaskStatus Status { get; set; } = TaskStatus.NotConfigured;
    public int? UserPriority { get; set; }
    public int? CalculatedPriority { get; set; }

    public int? Interest { get; set; }
    public int? Complexity { get; set; }
    public double? EstimatedHours { get; set; }
    public DateTime? Deadline { get; set; }
    public DateTime? PlannedDate { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsRecurring { get; set; }
    public RecurrencePattern? Recurrence { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }
    public int? ParentTaskId { get; set; }

    public DisplayType DisplayType { get; set; } = DisplayType.Independent;

    public bool IsProcrastinationResistant { get; set; }
    public DateTime? LastProcrastinatedDate { get; set; }
    public int ProcrastinationCount { get; set; }

    public List<Dependency> Dependencies { get; set; } = [];
    public List<Dependency> DependentTasks { get; set; } = [];
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public bool CanBeStarted()
    {
        var blockingDeps = Dependencies.Where(d => d.Type == DependencyType.Blocking).ToArray();
        return blockingDeps.Length is 0 || blockingDeps.All(d => d.TargetTask?.Status is TaskStatus.Completed);
    }

    public bool CanBeProcrastinated()
    {
        return Status == TaskStatus.Planned &&
               !IsProcrastinationResistant &&
               Complexity >= 70 &&
               ProcrastinationCount < 3;
    }
}

public class Dependency : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }
    public int SourceTaskId { get; set; }
    public int TargetTaskId { get; set; }

    public DependencyType Type { get; set; }
    public DependencyLogic Logic { get; set; } = DependencyLogic.And;
    public string? ConditionParameters { get; set; }
    public string? ConditionGroup { get; set; }
    public int ConditionOrder { get; set; }

    public TaskItem? SourceTask { get; set; }
    public TaskItem? TargetTask { get; set; }
}

public class UserSettings : IAuditEntity
{
    public int Id { get; set; } = 1;
    public DateTime LastChangesOn { get; set; }
    public int DayStartHour { get; set; } = 5;
    public double DailyTimeLimit { get; set; } = 8.0;
    public int DailyComplexityLimit { get; set; } = 50;
    public bool AutoRecalculateOnAdd { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public string? PriorityBoostDates { get; set; }
    public bool AutoCompleteGuaranteed { get; set; } = true;
    public bool RemoveUrgentIfNotDone { get; set; } = true;
    public bool ShowProcrastinationButton { get; set; } = true;
    public bool AutoBalanceTasks { get; set; } = true;
    public string? CustomPriorityReferences { get; set; }
}

public class RecurrencePattern
{
    public RecurrenceType Type { get; set; }
    public int Interval { get; set; } = 1;
    public List<DayOfWeek>? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
}
