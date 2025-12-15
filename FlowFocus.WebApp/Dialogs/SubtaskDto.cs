namespace FlowFocus.WebApp.Dialogs;

/// <summary>
/// DTO для подзадачи в диалоге редактирования
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

