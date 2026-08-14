namespace FlowFocus.Core.Enums;

/// <summary>
/// Тип метрики для гистограммы дашборда
/// </summary>
public enum HistogramMetricType
{
    /// <summary>Интересность (1-10)</summary>
    Interest,

    /// <summary>Сложность (1-100)</summary>
    Complexity,

    /// <summary>Приоритет</summary>
    Priority,

    /// <summary>Время выполнения в минутах</summary>
    Time,

    /// <summary>Интересность по приоритетам</summary>
    InterestPriority
}
