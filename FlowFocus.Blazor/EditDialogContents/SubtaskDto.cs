using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Blazor.EditDialogContents;

/// <summary>
/// DTO для подзадачи в диалоге редактирования
/// </summary>
public class SubtaskDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool HideUnderSpoiler { get; set; }
    public bool IsFavorite { get; set; }
    public int? Interest { get; set; }
    public int? Complexity { get; set; }
    public int? EstimatedMinutes { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Planned;
    public DateTime? CompletedDate { get; set; }
    public bool IsDeleted { get; set; }
}




