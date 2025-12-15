namespace FlowFocus.Core.Enums;

/// <summary>
/// Статусы задачи
/// </summary>
public enum TaskStatus
{
    /// <summary>Задача из быстрого ввода, требует настройки</summary>
    NotConfigured,
    /// <summary>Стандартный статус запланированной задачи</summary>
    Planned,
    /// <summary>Задача выполнена</summary>
    Completed,
    /// <summary>Задача помечена как неактуальная</summary>
    Irrelevant,
    /// <summary>Задача заблокирована другими задачами (визуальный статус)</summary>
    Blocked
}

/// <summary>
/// Тип связи между задачами
/// </summary>
public enum RelationType
{
    /// <summary>Задача связана с другой (без влияния на логику)</summary>
    RelatedTo,
    /// <summary>Задача блокирует другую</summary>
    Blocks,
    /// <summary>Задача заблокирована другой</summary>
    BlockedBy,
    /// <summary>Задача является подзадачей</summary>
    Subtask
}

/// <summary>
/// Режим отображения списка задач
/// </summary>
public enum DisplayMode
{
    /// <summary>Стандартный список</summary>
    List,
    /// <summary>Компактный список</summary>
    Compact,
    /// <summary>Сетка карточек</summary>
    Grid
}

/// <summary>
/// Тип повторения задачи
/// </summary>
public enum RecurrenceType
{
    /// <summary>Без повторения</summary>
    None,
    /// <summary>Ежедневно</summary>
    Daily,
    /// <summary>Каждые N дней</summary>
    EveryNDays,
    /// <summary>По дням недели</summary>
    WeekDays
}

/// <summary>
/// Формат времени выполнения
/// </summary>
public enum TimeFormat
{
    Minutes,
    Hours
}

/// <summary>
/// Тип сортировки списка задач
/// </summary>
public enum SortType
{
    /// <summary>По релевантности (приоритет, интерес, время)</summary>
    Relevance,
    /// <summary>По дате назначения (возр.)</summary>
    DateAsc,
    /// <summary>По дате назначения (убыв.)</summary>
    DateDesc,
    /// <summary>По сложности (возр.)</summary>
    ComplexityAsc,
    /// <summary>По сложности (убыв.)</summary>
    ComplexityDesc,
    /// <summary>По интересу (возр.)</summary>
    InterestAsc,
    /// <summary>По интересу (убыв.)</summary>
    InterestDesc,
    /// <summary>По времени выполнения (возр.)</summary>
    DurationAsc,
    /// <summary>По времени выполнения (убыв.)</summary>
    DurationDesc
}

/// <summary>
/// Фильтр по времени выполнения
/// </summary>
public enum DurationFilter
{
    /// <summary>Все задачи</summary>
    All,
    /// <summary>Короткие (до 10 мин)</summary>
    Short,
    /// <summary>Средние (до 1 часа)</summary>
    Medium,
    /// <summary>Долгие (до 12 часов)</summary>
    Long,
    /// <summary>Многодневные (более 12 часов)</summary>
    MultiDay
}
