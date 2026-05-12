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