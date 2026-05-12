namespace FlowFocus.Core.Models;

/// <summary>
/// DTO для создания/редактирования подзадачи (не сохраняется напрямую)
/// </summary>
public class SubtaskDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public int? Interest { get; set; }
    public int? Complexity { get; set; }
    public int? EstimatedMinutes { get; set; }
    public bool IsDeleted { get; set; }
}