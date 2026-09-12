namespace FlowFocus.Core.Enums;

/// <summary>
/// Расширения статусов задач для определения роли задачи в жизненном цикле приложения.
/// </summary>
public static class TaskStatusExtensions
{
    /// <summary>
    /// Неактивные задачи (завершённые, неактуальные, ненастроенные).
    /// Являются условно "readonly" для всех автоматических алгоритмов приложения.
    /// Автоматические переносы, эскалации и динамические блокировки их не затрагивают.
    /// </summary>
    public static bool IsInactive(this TaskStatus status) =>
        status is TaskStatus.Completed or TaskStatus.Irrelevant or TaskStatus.NotConfigured;

    /// <summary>
    /// Активные задачи (запланированные, заблокированные).
    /// Участвуют в алгоритмическом планировании, распределении дат, наследовании приоритетов и динамических блокировках.
    /// </summary>
    public static bool IsActive(this TaskStatus status) => !status.IsInactive();
}
