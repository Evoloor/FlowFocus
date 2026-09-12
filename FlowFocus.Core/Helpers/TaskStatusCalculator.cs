using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Helpers;

/// <summary>
/// Единый калькулятор проверки заблокированного состояния задач
/// </summary>
public static class TaskStatusCalculator
{
    /// <summary>
    /// Проверяет, заблокирована ли задача внешними условиями или задачами-блокерами
    /// </summary>
    public static bool IsTaskBlocked(TaskItem task)
    {
        if (task.IsInactive)
            return false;

        return task.IsBlocked;
    }

    /// <summary>
    /// Определяет актуальный статус активной задачи.
    /// Если задача неактивна (Completed, Irrelevant, NotConfigured) — сохраняет её текущий статус без изменений.
    /// Если активная задача заблокирована — возвращает TaskStatus.Blocked.
    /// Если разблокирована и предыдущий статус был Blocked — возвращает defaultUnblockedStatus (по умолчанию Planned).
    /// </summary>
    public static TaskStatus DetermineActiveStatus(TaskItem task, TaskStatus defaultUnblockedStatus = TaskStatus.Planned)
    {
        if (task.IsInactive)
            return task.Status;

        if (IsTaskBlocked(task))
            return TaskStatus.Blocked;

        return task.Status == TaskStatus.Blocked ? defaultUnblockedStatus : task.Status;
    }
}
