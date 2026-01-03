using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

public interface IAuditEntity
{
    int Id { get; set; }
    DateTime LastChangesOn { get; set; }
}

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
    /// <summary>Дата назначения, установленная пользователем</summary>
    public DateTime? UserAssignedDate { get; set; }

    /// <summary>Фактическая дата назначения (устанавливается алгоритмом или равна UserAssignedDate)</summary>
    public DateTime? ActualAssignedDate { get; set; }

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
        UserAssignedDate = source.UserAssignedDate;
        ActualAssignedDate = source.ActualAssignedDate;
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

/// <summary>
/// Тег задачи
/// </summary>
public class Tag : IAuditEntity
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

/// <summary>
/// Связь между задачами
/// </summary>
public class TaskRelation : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    /// <summary>ID исходной задачи</summary>
    public int SourceTaskId { get; set; }

    [ForeignKey(nameof(SourceTaskId))]
    public TaskItem? SourceTask { get; init; }

    /// <summary>ID целевой задачи</summary>
    public int TargetTaskId { get; init; }

    [ForeignKey(nameof(TargetTaskId))]
    public TaskItem? TargetTask { get; init; }

    /// <summary>Тип связи</summary>
    public RelationType Type { get; init; }
}

/// <summary>
/// Правило повышения приоритета
/// </summary>
public class PriorityEscalation : IAuditEntity
{
    public int Id { get; set; }
    public DateTime LastChangesOn { get; set; }

    public int TaskId { get; set; }
    [ForeignKey(nameof(TaskId))]
    public TaskItem? Task { get; init; }

    /// <summary>ID приоритета, до которого повышается задача</summary>
    public int TargetPriorityId { get; init; }

    [ForeignKey(nameof(TargetPriorityId))]
    public PriorityLevel? TargetPriority { get; init; }

    /// <summary>Дата, когда применяется повышение</summary>
    public DateTime EscalationDate { get; init; }

    /// <summary>Применено ли уже повышение</summary>
    public bool IsApplied { get; set; }
}

    /// <summary>
    /// Настройки пользователя
    /// </summary>
    public class UserSettings : IAuditEntity
    {
        public int Id { get; set; } = 1;
        public DateTime LastChangesOn { get; set; }

        // === Время ===
        /// <summary>Время начала дня (часы, 0-23)</summary>
        [Range(0, 23)]
        public int DayStartHour { get; set; } = 5;

        // === Лимиты на день ===
        /// <summary>Максимальная суммарная сложность задач на день</summary>
        [Range(1, 1000)]
        public int DailyComplexityLimit { get; set; } = 100;

        /// <summary>Максимальное суммарное время задач на день (минуты)</summary>
        [Range(1, 1440)]
        public int DailyTimeLimit { get; set; } = 480;

        /// <summary>Максимальное количество задач на день</summary>
        [Range(1, 100)]
        public int DailyTaskLimit { get; set; } = 10;

        // === Авто-распределение ===
        /// <summary>Включено ли авто-распределение при добавлении/изменении задач</summary>
        public bool AutoDistributeEnabled { get; set; }

        // === Интерфейс ===
        /// <summary>Тёмная тема</summary>
        public bool IsDarkMode { get; set; } = true;

        /// <summary>Скрывать названия задач под спойлер по умолчанию</summary>
        public bool HideTaskTitlesDefault { get; set; }

        /// <summary>ID приоритета по умолчанию</summary>
        public int? DefaultPriorityId { get; set; }
}

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
