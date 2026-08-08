namespace FlowFocus.Core.Models;

/// <summary>
/// Внешнее условие (например, «нахождение в городе А», «доступ к домашнему ПК»)
/// </summary>
public class ExternalCondition : TaskLabelBase
{
    /// <summary>
    /// Флаг активности условия (тумблер в настройках).
    /// Если false — все зависящие от этого условия задачи блокируются.
    /// </summary>
    public bool IsActive { get; set; } = false;
}
