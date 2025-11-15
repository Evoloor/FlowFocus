using System.ComponentModel.DataAnnotations;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

public class TaskItem
{
    public int Id { get; set; }
    
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    
    public TaskStatus Status { get; set; } = TaskStatus.NotConfigured;
    public int UserPriority { get; set; } = 5;
    public int CalculatedPriority { get; set; }
    
    public int Interest { get; set; } = 5;
    public int Complexity { get; set; } = 5;
    public double EstimatedHours { get; set; } = 1.0;
    
    public DateTime? Deadline { get; set; }
    public DateTime? PlannedDate { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    
    public bool IsFavorite { get; set; }
    public bool IsRecurring { get; set; }
    public RecurrencePattern? Recurrence { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }
    public int? ParentTaskId { get; set; }
    
    public DisplayType DisplayType { get; set; } = DisplayType.Independent;
    
    public List<Dependency> Dependencies { get; set; } = [];
    public List<Dependency> DependentTasks { get; set; } = [];
    
    // Метод для проверки, можно ли выполнить задачу
    public bool CanBeStarted()
    {
        var blockingDeps = Dependencies?.Where(d => d.Type == DependencyType.Blocking) ?? new List<Dependency>();
        return !blockingDeps.Any() || blockingDeps.All(d => d.TargetTask?.Status == TaskStatus.Completed);
    }
}

public class Dependency
{
    public int Id { get; set; }
    public int SourceTaskId { get; set; }
    public int TargetTaskId { get; set; }
    
    public DependencyType Type { get; set; }
    public DependencyLogic Logic { get; set; } = DependencyLogic.And;
    public string? ConditionParameters { get; set; }
    public string? ConditionGroup { get; set; }
    public int ConditionOrder { get; set; }
    
    // Navigation properties
    public TaskItem? SourceTask { get; set; }
    public TaskItem? TargetTask { get; set; }
}

public class UserSettings
{
    public int Id { get; set; } = 1;
    public int DayStartHour { get; set; } = 6;
    public double DailyTimeLimit { get; set; } = 8.0;
    public int DailyComplexityLimit { get; set; } = 50;
    public bool AutoRecalculateOnAdd { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public string? PriorityBoostDates { get; set; }
    public bool AutoCompleteGuaranteed { get; set; } = true;
    public bool RemoveUrgentIfNotDone { get; set; } = true;
}

public class RecurrencePattern
{
    public RecurrenceType Type { get; set; }
    public int Interval { get; set; } = 1;
    public List<DayOfWeek>? DaysOfWeek { get; set; }
    public int? DayOfMonth { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
}

public enum RecurrenceType
{
    Daily,
    Weekly,
    Monthly,
    Yearly
}