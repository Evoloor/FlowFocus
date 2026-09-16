using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

/// <summary>
/// Абстрактный базовый класс для задач и подзадач.
/// Содержит общие поля, применимые к обоим типам рабочих элементов.
/// </summary>
public abstract class WorkItemBase : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    // === Основные поля ===
    [Required(ErrorMessage = "Название обязательно")]
    [StringLength(500, ErrorMessage = "Название не должно превышать 500 символов")]
    public string Title { get; set; } = string.Empty;

    [StringLength(5000)]
    public string? Description { get; set; }

    /// <summary>Скрывать название/описание под спойлер</summary>
    public bool HideUnderSpoiler { get; set; }

    /// <summary>Статус задачи</summary>
    public TaskStatus Status { get; set; } = TaskStatus.NotConfigured;

    /// <summary>Избранная задача</summary>
    public bool IsFavorite { get; set; }

    // === Оценки ===
    /// <summary>Интересность задачи (1-10)</summary>
    [Range(1, 10)]
    public int? Interest { get; set; }

    /// <summary>Сложность задачи (1-100)</summary>
    [Range(1, 100)]
    public int? Complexity { get; set; }

    /// <summary>Время выполнения в минутах</summary>
    [Range(1, 10000)]
    public int? EstimatedMinutes { get; set; }

    // === Даты ===
    /// <summary>Дата завершения задачи</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Дата создания задачи</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // === Вычисляемые свойства ===
    /// <summary>Признак неактивной задачи (Completed, Irrelevant, NotConfigured). Условно readonly для автоматики.</summary>
    [NotMapped]
    public bool IsInactive => Status.IsInactive();

    /// <summary>Признак активной задачи (Planned, Blocked).</summary>
    [NotMapped]
    public bool IsActive => Status.IsActive();

    /// <summary>Условный readonly для всех автоматических фоновых алгоритмов.</summary>
    [NotMapped]
    public bool IsReadOnlyForAutomation => IsInactive;

    [NotMapped]
    public string FormattedDuration
    {
        get
        {
            if (EstimatedMinutes == null) return string.Empty;
            if (EstimatedMinutes < 60) return $"{EstimatedMinutes} мин";
            var hours = EstimatedMinutes.Value / 60;
            var minutes = EstimatedMinutes.Value % 60;
            return minutes > 0 ? $"{hours} ч {minutes} мин" : $"{hours} ч";
        }
    }
}
