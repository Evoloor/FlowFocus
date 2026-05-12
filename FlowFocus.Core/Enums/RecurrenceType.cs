namespace FlowFocus.Core.Enums;

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
    WeekDays,
    /// <summary>Ежемесячно</summary>
    Monthly,
    /// <summary>Ежегодно</summary>
    Yearly
}