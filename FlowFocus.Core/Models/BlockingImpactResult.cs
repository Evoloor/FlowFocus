namespace FlowFocus.Core.Models;

/// <summary>
/// Результат анализа транзитивного влияния задачи-блокера
/// </summary>
public sealed record BlockingImpactResult(TaskItem BlockerTask, int BlockedTasksCount);
