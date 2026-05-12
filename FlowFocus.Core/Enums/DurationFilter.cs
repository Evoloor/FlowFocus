namespace FlowFocus.Core.Enums;

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