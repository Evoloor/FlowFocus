namespace FlowFocus.Core.Enums;

/// <summary>
/// Тип повторения задачи
/// </summary>
public enum RecurrenceType
{
    /// <summary>Без повторения</summary>
    None = 0,
    /// <summary>Периодически каждые N (дней/месяцев/лет)</summary>
    EveryN = 1,
    /// <summary>По дням недели</summary>
    WeekDays = 3
}