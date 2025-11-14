using System.ComponentModel.DataAnnotations;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
namespace FlowFocus.Core.Models;
public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [Required]
    [MaxLength(500)]
    public string Title { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    
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
    
    public DisplayType DisplayType { get; set; } = DisplayType.Independent;
    
    public List<Dependency> Dependencies { get; set; } = new();
    public List<Dependency> DependentTasks { get; set; } = new();
}
public class Dependency
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceTaskId { get; set; }
    public Guid TargetTaskId { get; set; }
    
    public DependencyType Type { get; set; }
    public DependencyLogic Logic { get; set; } = DependencyLogic.AND;
    public string? ConditionParameters { get; set; }
    
    // Navigation properties
    public TaskItem? SourceTask { get; set; }
    public TaskItem? TargetTask { get; set; }
}
public class UserSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int DayStartHour { get; set; } = 6;
    public double DailyTimeLimit { get; set; } = 8.0;
    public int DailyComplexityLimit { get; set; } = 50;
    public bool AutoRecalculateOnAdd { get; set; } = true;
    public bool ShowFavorites { get; set; } = true;
    public string? PriorityBoostDates { get; set; }
}
