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

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string? Description { get; set; }

    public List<string> Tags { get; set; } = new();

    public int Interest { get; set; } // 1–10
    public int Complexity { get; set; } // 1–100
    public double Hours { get; set; } // up to 1000

    public DateTime? Deadline { get; set; }
    public DateTime? AssignedDate { get; set; }
    public DateTime? LastChange { get; set; }

    public TodoTaskStatus Status { get; set; } = TodoTaskStatus.Unconfigured;
    public bool IsFavorite { get; set; } = false;

    public RepeatInfo? Repeat { get; set; }

    public List<TaskBlocker> Blockers { get; set; } = new();
}

public class TaskBlocker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ParentTaskId { get; set; }
    public Guid BlockerTaskId { get; set; }
    public BlockerType Type { get; set; } = BlockerType.And;
}

public class RepeatInfo
{
    public RepeatType Type { get; set; } = RepeatType.None;
    public int Interval { get; set; } = 0; // N hours/days
    public List<DayOfWeek>? DaysOfWeek { get; set; } // for weekly repeats
}

public class UserAppSettings
{
    public TimeSpan DayStartTime { get; set; } = new(5, 0, 0); // default 5:00
    public double DailyHoursLimit { get; set; } = 8.0;
    public int DailyComplexityLimit { get; set; } = 300;
    public bool AutoRecalculateOnAdd { get; set; } = true;
}

public class AppState
{
    public DateTime CurrentDay { get; set; } = DateTime.Today;
    public List<TaskItem> Tasks { get; set; } = new();
    public UserAppSettings Settings { get; set; } = new();
}