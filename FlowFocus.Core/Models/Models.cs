namespace FlowFocus.Core.Models;

public enum TodoTaskStatus
{
    Unconfigured,
    Planned,
    Active,
    Completed
}

public enum RepeatType
{
    None,
    EveryNHours,
    EveryNDays,
    WeeklyDays
}

public enum BlockerType
{
    And,
    Or
}

public abstract class AuditEntity
{
    public int Id { get; set; }
    public DateTime LastChange { get; set; } = DateTime.Now;
}

public class TaskItem : AuditEntity
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public List<string> Tags { get; set; } = [];

    public int Interest { get; set; } // 1–10
    public int Complexity { get; set; } // 1–100
    public double Hours { get; set; } // up to 1000

    public DateTime? Deadline { get; set; }
    public DateTime? AssignedDate { get; set; }

    public TodoTaskStatus Status { get; set; } = TodoTaskStatus.Unconfigured;
    public bool IsFavorite { get; set; } = false;

    public RepeatInfo? Repeat { get; set; }

    public List<TaskBlocker> Blockers { get; set; } = [];
}

public class TaskBlocker : AuditEntity
{
    public int ParentTaskId { get; set; }
    public int BlockerTaskId { get; set; }
    public BlockerType Type { get; set; } = BlockerType.And;
}

public class RepeatInfo : AuditEntity
{
    public RepeatType Type { get; set; } = RepeatType.None;
    public int Interval { get; set; } = 0; // N hours/days
    public List<DayOfWeek>? DaysOfWeek { get; set; } // for weekly repeats
    public List<int>? DaysOfMonth { get; set; } // for monthly repeats
}

public class UserAppSettings : AuditEntity
{
    private static readonly TimeSpan DefaultDayStartTime = new(5, 0, 0);
    public TimeSpan DayStartTime { get; set; } = DefaultDayStartTime;
    private static readonly double DefaultDailyHoursLimit = 8.0;
    public double DailyHoursLimit { get; set; } = DefaultDailyHoursLimit;
    private static readonly int DefaultDailyComplexityLimit = 300;
    public int DailyComplexityLimit { get; set; } = DefaultDailyComplexityLimit;
    public static readonly bool DefaultAutoRecalculateOnAdd = true;
    public bool AutoRecalculateOnAdd { get; set; } = DefaultAutoRecalculateOnAdd;

    public void ResetToDefaults()
    {
        DayStartTime = DefaultDayStartTime;
        DailyHoursLimit = DefaultDailyHoursLimit;
        DailyComplexityLimit = DefaultDailyComplexityLimit;
        AutoRecalculateOnAdd = DefaultAutoRecalculateOnAdd;
    }
}
