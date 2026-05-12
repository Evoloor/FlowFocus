namespace FlowFocus.Core.Enums;

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