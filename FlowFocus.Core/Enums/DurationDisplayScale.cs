namespace FlowFocus.Core.Enums;

/// <summary>
/// Масштаб отображения длительности в элементах дашборда
/// </summary>
public enum DurationDisplayScale
{
    /// <summary>Подробный формат («2 ч 15 мин») для широких таблиц и тултипов</summary>
    Detailed,

    /// <summary>Компактный формат в минутах («45 мин»)</summary>
    CompactMinutes,

    /// <summary>Компактный формат в часах («0.5 ч», «12.5 ч», «468 ч»)</summary>
    CompactHours
}
