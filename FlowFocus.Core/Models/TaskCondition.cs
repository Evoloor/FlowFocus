using System.ComponentModel.DataAnnotations.Schema;

namespace FlowFocus.Core.Models;

/// <summary>
/// Связь задачи с внешним условием
/// </summary>
public class TaskCondition
{
    public int Id { get; init; }

    public int TaskId { get; init; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem Task { get; init; } = null!;

    public int ConditionId { get; init; }
    [ForeignKey(nameof(ConditionId))]
    public ExternalCondition Condition { get; init; } = null!;
}
