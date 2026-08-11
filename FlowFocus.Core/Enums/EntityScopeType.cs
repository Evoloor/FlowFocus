namespace FlowFocus.Core.Enums;

/// <summary>
/// Область фильтрации сущностей в дашборде
/// </summary>
public enum EntityScopeType
{
    /// <summary>Все задачи</summary>
    All,
    /// <summary>Активные задачи (не завершены и не неактуальны)</summary>
    Active,
    /// <summary>Завершённые задачи</summary>
    Completed,
    /// <summary>Фильтр по тегу</summary>
    Tag,
    /// <summary>Фильтр по условию</summary>
    Condition
}
