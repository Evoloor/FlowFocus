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
    private DateTime? _scheduledDate;

    /// <summary>
    /// Единственная дата планирования задачи.
    /// Для подзадачи ссылается (лайв-биндится) на дату родительской задачи.
    /// </summary>
    /// <remarks>
    /// Для подзадачи чтение всегда возвращает актуальную дату родителя (<see cref="ParentTask"/>).
    /// Присвоение даты подзадаче ни при каких обстоятельствах не изменяет родительскую задачу.
    /// </remarks>
    public DateTime? ScheduledDate
    {
        get => ParentTask != null ? ParentTask.ScheduledDate : _scheduledDate;
        set => _scheduledDate = value;
    }

    private DateSource _dateSource = DateSource.AutoFlexible;

    /// <summary>
    /// Определяет, кем и как была назначена дата <see cref="ScheduledDate"/>.
    /// Для подзадачи ссылается (лайв-биндится) на источник даты родительской задачи.
    /// </summary>
    /// <remarks>
    /// Для подзадачи чтение всегда возвращает актуальный источник даты родителя (<see cref="ParentTask"/>).
    /// Присвоение источника даты подзадаче ни при каких обстоятельствах не изменяет родительскую задачу.
    /// </remarks>
    public DateSource DateSource
    {
        get => ParentTask != null ? ParentTask.DateSource : _dateSource;
        set => _dateSource = value;
    }

    /// <summary>Дата завершения задачи</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Дата создания задачи</summary>
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

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
    private TaskItem? _parentTask;

    /// <summary>ID родительской задачи (если это подзадача)</summary>
    public int? ParentTaskId { get; set; }

    [ForeignKey(nameof(ParentTaskId))]
    public TaskItem? ParentTask
    {
        get => _parentTask;
        set
        {
            _parentTask = value;
            if (value != null && value.Id != 0)
            {
                ParentTaskId = value.Id;
            }
        }
    }

    private List<TaskItem> _subtasks = [];

    /// <summary>Подзадачи</summary>
    public List<TaskItem> Subtasks
    {
        get => _subtasks;
        set
        {
            _subtasks = value ?? [];
            foreach (var subtask in _subtasks)
            {
                subtask.ParentTask = this;
                if (Id != 0 && subtask.ParentTaskId == null)
                {
                    subtask.ParentTaskId = Id;
                }
            }
        }
    }

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
    public bool IsBlocked =>
        // Учёт через обратные связи (блокеры) или неактивные внешние условия
        InverseRelations.Any(r => r.Type == RelationType.Blocks &&
                                   r.SourceTask?.Status != TaskStatus.Completed &&
                                   r.SourceTask?.Status != TaskStatus.Irrelevant)
        || Conditions.Any(c => c.Condition != null && !c.Condition.IsActive);

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
        Interest = source.Interest;
        Complexity = source.Complexity;
        EstimatedMinutes = source.EstimatedMinutes;
        ParentTaskId = source.ParentTaskId;
        ParentTask = source.ParentTask;
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
