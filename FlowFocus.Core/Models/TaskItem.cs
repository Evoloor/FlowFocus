using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

/// <summary>
/// Основная модель задачи
/// </summary>
public class TaskItem : IAuditEntity
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

    // === Приоритет ===
    /// <summary>ID приоритета, установленного пользователем (null = не установлен)</summary>
    public int? PriorityId { get; set; }

    /// <summary>Связанный приоритет</summary>
    [ForeignKey(nameof(PriorityId))]
    public PriorityLevel? Priority { get; init; }

    /// <summary>Текущий эффективный приоритет (после применения повышений)</summary>
    public int? EffectivePriorityId { get; set; }

    [ForeignKey(nameof(EffectivePriorityId))]
    public PriorityLevel? EffectivePriority { get; init; }

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
    /// <summary>
    /// Единственная дата планирования задачи.
    /// Её источник и поведение определяются полем <see cref="DateSource"/>.
    /// </summary>
    public DateTime? ScheduledDate { get; set; }

    /// <summary>
    /// Определяет, кем и как была назначена дата <see cref="ScheduledDate"/>.
    /// </summary>
    public DateSource DateSource { get; set; } = DateSource.AutoFlexible;

    /// <summary>Дата завершения задачи</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Дата создания задачи</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    // === Повторение ===
    /// <summary>Включено ли повторение</summary>
    public bool IsRecurring { get; set; }

    /// <summary>Тип повторения</summary>
    public RecurrenceType RecurrenceType { get; set; } = RecurrenceType.None;

    /// <summary>Интервал повторения (для EveryNDays)</summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary>Дни недели для повторения (битовая маска: 1=Пн, 2=Вт, 4=Ср, 8=Чт, 16=Пт, 32=Сб, 64=Вс)</summary>
    public int? RecurrenceWeekDays { get; set; }

    /// <summary>ID родительской повторяющейся задачи</summary>
    public int? RecurrenceSourceId { get; init; }

    // === Связи ===
    /// <summary>ID родительской задачи (если это подзадача)</summary>
    public int? ParentTaskId { get; set; }

    [ForeignKey(nameof(ParentTaskId))]
    public TaskItem? ParentTask { get; init; }

    /// <summary>Подзадачи</summary>
    public List<TaskItem> Subtasks { get; set; } = [];

    /// <summary>Теги задачи</summary>
    public List<TaskTag> Tags { get; set; } = [];

    /// <summary>Связи с другими задачами</summary>
    public List<TaskRelation> Relations { get; set; } = [];

    /// <summary>Обратные связи (задачи, которые ссылаются на эту)</summary>
    public List<TaskRelation> InverseRelations { get; init; } = [];

    /// <summary>Правила повышения приоритета</summary>
    public List<PriorityEscalation> PriorityEscalations { get; set; } = [];

    // === Вычисляемые свойства ===
    [NotMapped]
    public bool IsBlocked =>
        // Учёт через обратные связи, где другая задача имеет тип Blocks (SourceTask -> this)
        InverseRelations.Any(r => r.Type == RelationType.Blocks &&
                                   r.SourceTask?.Status != TaskStatus.Completed &&
                                   r.SourceTask?.Status != TaskStatus.Irrelevant);

    [NotMapped]
    public bool IsSubtask => ParentTaskId != null;

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

    /// <summary>
    /// Суммарное время (включая подзадачи)
    /// </summary>
    [NotMapped]
    public int TotalEstimatedMinutes => (EstimatedMinutes ?? 0) + Subtasks.Sum(s => s.EstimatedMinutes ?? 0);

    /// <summary>
    /// Суммарная сложность (включая подзадачи)
    /// </summary>
    [NotMapped]
    public int TotalComplexity => (Complexity ?? 0) + Subtasks.Sum(s => s.Complexity ?? 0);

    /// <summary>
    /// Конструктор копирования
    /// </summary>
    public TaskItem() { }

    public TaskItem(TaskItem source)
    {
        Id = source.Id;
        LastChangesOn = source.LastChangesOn;
        Title = source.Title;
        Description = source.Description;
        HideUnderSpoiler = source.HideUnderSpoiler;
        Status = source.Status;
        IsFavorite = source.IsFavorite;
        PriorityId = source.PriorityId;
        EffectivePriorityId = source.EffectivePriorityId;
        Interest = source.Interest;
        Complexity = source.Complexity;
        EstimatedMinutes = source.EstimatedMinutes;
        ScheduledDate = source.ScheduledDate;
        DateSource = source.DateSource;
        CompletedDate = source.CompletedDate;
        CreatedDate = source.CreatedDate;
        IsRecurring = source.IsRecurring;
        RecurrenceType = source.RecurrenceType;
        RecurrenceInterval = source.RecurrenceInterval;
        RecurrenceWeekDays = source.RecurrenceWeekDays;
        RecurrenceSourceId = source.RecurrenceSourceId;
        ParentTaskId = source.ParentTaskId;
    }
}