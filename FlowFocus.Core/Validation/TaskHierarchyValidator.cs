using FlowFocus.Core.Models;

namespace FlowFocus.Core.Validation;

/// <summary>
/// Валидатор иерархии задач и подзадач
/// </summary>
public static class TaskHierarchyValidator
{
    /// <summary>
    /// Проверка допустимости подзадачи для родительской задачи
    /// </summary>
    public static void ValidateSubtaskParent(TaskItem parentTask, SubtaskItem subtask)
    {
        ArgumentNullException.ThrowIfNull(parentTask);
        ArgumentNullException.ThrowIfNull(subtask);

        if (subtask.ParentTaskId != 0 && parentTask.Id != 0 && subtask.ParentTaskId != parentTask.Id)
        {
            throw new InvalidOperationException("Подзадача привязана к другой родительской задаче.");
        }
    }
}
