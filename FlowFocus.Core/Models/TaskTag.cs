using System.ComponentModel.DataAnnotations.Schema;

namespace FlowFocus.Core.Models;

/// <summary>
/// Связь задачи с тегом
/// </summary>
public class TaskTag
{
    public int Id { get; init; }

    public int TaskId { get; init; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem Task { get; init; } = null!;

    public int TagId { get; init; }
    [ForeignKey(nameof(TagId))]
    public Tag Tag { get; init; } = null!;
}