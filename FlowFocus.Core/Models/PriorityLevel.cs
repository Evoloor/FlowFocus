using System.ComponentModel.DataAnnotations;

namespace FlowFocus.Core.Models;

/// <summary>
/// Уровень приоритета (настраиваемый)
/// </summary>
public class PriorityLevel : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    /// <summary>Порядковый номер (чем меньше, тем важнее)</summary>
    public int Order { get; set; }

    /// <summary>Название приоритета</summary>
    [Required]
    [StringLength(50)]
    public string Name { get; init; } = string.Empty;

    /// <summary>Цвет в формате HEX</summary>
    [StringLength(9)]
    public string Color { get; init; } = "#808080";
}