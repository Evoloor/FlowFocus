using System.ComponentModel.DataAnnotations.Schema;

namespace FlowFocus.Core.Models;

/// <summary>
/// Правило повышения приоритета
/// </summary>
public class PriorityEscalation : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    public int TaskId { get; set; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem? Task { get; init; }

    /// <summary>ID приоритета, до которого повышается задача</summary>
    public int TargetPriorityId { get; init; }

    [ForeignKey(nameof(TargetPriorityId))]
    public PriorityLevel? TargetPriority { get; init; }

    /// <summary>Дата, когда применяется повышение</summary>
    public DateTime EscalationDate { get; init; }

    /// <summary>Применено ли уже повышение</summary>
    public bool IsApplied { get; set; }
}