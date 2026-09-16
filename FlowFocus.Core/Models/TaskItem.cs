using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

/// <summary>
/// Основная модель задачи (верхнеуровневый рабочий элемент).
/// Не может быть подзадачей — для этого используется <see cref="SubtaskItem"/>.
/// </summary>
public class TaskItem : WorkItemBase
{
    // === Приоритет ===
    /// <summary>ID приоритета, установленного пользователем (null = не установлен)</summary>
    public int? PriorityId { get; set; }

    /// <summary>Связанный приоритет</summary>
    [ForeignKey(nameof(PriorityId))]
    public PriorityLevel? Priority { get; init; }

    // === Даты ===
    /// <summary>Дата планирования задачи</summary>
    public DateTime? ScheduledDate { get; set; }

    /// <summary>Определяет, кем и как была назначена дата <see cref="ScheduledDate"/>.</summary>
    public DateSource DateSource { get; set; } = DateSource.AutoFlexible;

    // === Повторение ===
    /// <summary>Включено ли повторение</summary>
    public bool IsRecurring { get; set; }

    private RecurrenceType _recurrenceType = RecurrenceType.None;

    /// <summary>Тип повторения</summary>
    public RecurrenceType RecurrenceType
    {
        get => _recurrenceType;
        set
        {
            switch ((int)value)
            {
                case 1: // Legacy Daily or EveryN
                    _recurrenceType = RecurrenceType.EveryN;
                    break;
                case 2: // Legacy EveryNDays
                    _recurrenceType = RecurrenceType.EveryN;
                    RecurrenceUnit = RecurrenceUnit.Days;
                    RecurrenceInterval ??= 1;
                    break;
                case 4: // Legacy Monthly
                    _recurrenceType = RecurrenceType.EveryN;
                    RecurrenceUnit = RecurrenceUnit.Months;
                    RecurrenceInterval ??= 1;
                    break;
                case 5: // Legacy Yearly
                    _recurrenceType = RecurrenceType.EveryN;
                    RecurrenceUnit = RecurrenceUnit.Years;
                    RecurrenceInterval ??= 1;
                    break;
                default:
                    _recurrenceType = value;
                    break;
            }
        }
    }

    /// <summary>Единица измерения интервала повторения</summary>
    public RecurrenceUnit RecurrenceUnit { get; set; } = RecurrenceUnit.Days;

    private int? _recurrenceInterval;

    /// <summary>Интервал повторения (для EveryN)</summary>
    public int? RecurrenceInterval
    {
        get => _recurrenceType == RecurrenceType.EveryN && (_recurrenceInterval == null || _recurrenceInterval <= 0)
            ? 1
            : _recurrenceInterval;
        set => _recurrenceInterval = value;
    }

    /// <summary>Дни недели для повторения (битовая маска: 1=Пн, 2=Вт, 4=Ср, 8=Чт, 16=Пт, 32=Сб, 64=Вс)</summary>
    public int? RecurrenceWeekDays { get; set; }

    /// <summary>ID родительской повторяющейся задачи</summary>
    public int? RecurrenceSourceId { get; init; }

    // === Связи ===
    /// <summary>Подзадачи</summary>
    public List<SubtaskItem> Subtasks { get; set; } = [];

    /// <summary>Теги задачи</summary>
    public List<TaskTag> Tags { get; set; } = [];

    /// <summary>Внешние условия задачи</summary>
    public List<TaskCondition> Conditions { get; set; } = [];

    /// <summary>Связи с другими задачами</summary>
    public List<TaskRelation> Relations { get; set; } = [];

    /// <summary>Обратные связи (задачи, которые ссылаются на эту)</summary>
    public List<TaskRelation> InverseRelations { get; set; } = [];

    /// <summary>Правила повышения приоритета</summary>
    public List<PriorityEscalation> PriorityEscalations { get; set; } = [];

    // === Вычисляемые свойства ===
    [NotMapped]
    public bool IsBlocked =>
        // Учёт через обратные связи (блокеры) или неактивные внешние условия
        InverseRelations.Any(r => r.Type == RelationType.Blocks &&
                                   r.SourceTask?.Status != TaskStatus.Completed &&
                                   r.SourceTask?.Status != TaskStatus.Irrelevant)
        || Conditions.Any(c => c.Condition != null && !c.Condition.IsActive);

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
    /// Конструктор по умолчанию
    /// </summary>
    public TaskItem() { }

    /// <summary>
    /// Конструктор копирования
    /// </summary>
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
        Interest = source.Interest;
        Complexity = source.Complexity;
        EstimatedMinutes = source.EstimatedMinutes;
        ScheduledDate = source.ScheduledDate;
        DateSource = source.DateSource;
        CompletedDate = source.CompletedDate;
        CreatedDate = source.CreatedDate;
        IsRecurring = source.IsRecurring;
        RecurrenceType = source.RecurrenceType;
        RecurrenceUnit = source.RecurrenceUnit;
        RecurrenceInterval = source.RecurrenceInterval;
        RecurrenceWeekDays = source.RecurrenceWeekDays;
        RecurrenceSourceId = source.RecurrenceSourceId;
    }
}
