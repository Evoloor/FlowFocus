using System.ComponentModel.DataAnnotations;

namespace FlowFocus.Core.Models;

/// <summary>
/// Базовый класс для меток задач (теги, внешние условия)
/// </summary>
public abstract class TaskLabelBase : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    [Required]
    [StringLength(50)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Цвет фона (пастельный оттенок)</summary>
    [StringLength(9)]
    public string BackgroundColor { get; init; } = "#E8E8E8";

    /// <summary>Количество использований</summary>
    public int UsageCount { get; set; }

    /// <summary>Дата последнего использования</summary>
    public DateTime? LastUsedDate { get; set; }
}
