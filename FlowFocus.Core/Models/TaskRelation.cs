using System.ComponentModel.DataAnnotations.Schema;
using FlowFocus.Core.Enums;

namespace FlowFocus.Core.Models;

/// <summary>
/// Связь между задачами
/// </summary>
public class TaskRelation : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    /// <summary>ID исходной задачи</summary>
    public int SourceTaskId { get; set; }

    [ForeignKey(nameof(SourceTaskId))]
    public TaskItem? SourceTask { get; init; }

    /// <summary>ID целевой задачи</summary>
    public int TargetTaskId { get; init; }

    [ForeignKey(nameof(TargetTaskId))]
    public TaskItem? TargetTask { get; init; }

    /// <summary>Тип связи</summary>
    public RelationType Type { get; init; }
}