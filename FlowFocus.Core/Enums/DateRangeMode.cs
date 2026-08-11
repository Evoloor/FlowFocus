namespace FlowFocus.Core.Enums;

/// <summary>
/// Варианты временного отрезка для фильтрации дашборда
/// </summary>
public enum DateRangeMode
{
    /// <summary>
    /// За всё время (по умолчанию)
    /// </summary>
    AllTime,

    /// <summary>
    /// Недавние: Math.max(задачи за последние 14 дней, последние 100 выполненных задач)
    /// </summary>
    Recent,

    /// <summary>
    /// Показательные: Math.max(задачи за последние 3 месяца / 90 дней, последние 300 выполненных задач)
    /// </summary>
    Representative
}
