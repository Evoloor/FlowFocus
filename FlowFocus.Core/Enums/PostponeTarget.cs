namespace FlowFocus.Core.Enums;

/// <summary>
/// Целевое направление переноса / откладывания задачи
/// </summary>
public enum PostponeTarget
{
    /// <summary>
    /// Автоматическое распределение по правилам (для гибких — бэклог с отсрочкой, для повторов — следующий допустимый день)
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Строго на завтра с ручной фиксацией даты (DateSource.Manual)
    /// </summary>
    StrictTomorrow = 1
}
