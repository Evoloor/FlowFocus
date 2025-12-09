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
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }
}

public class UserSettings : IAuditEntity
{
    public int Id { get; set; } = 1;
    public DateTime LastChangesOn { get; set; }
}

public class RecurrencePattern
{
}
