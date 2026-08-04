using FlowFocus.Core.Models;

namespace FlowFocus.Core.Validation;

/// <summary>
/// Валидатор иерархии задач и подзадач
/// </summary>
public static class TaskHierarchyValidator
{
    /// <summary>
    /// Проверка допустимости присвоения parentTaskId для задачи childTask
    /// </summary>
    public static void ValidateSubtaskParent(TaskItem parentTask, TaskItem childTask)
    {
        ArgumentNullException.ThrowIfNull(parentTask);
        ArgumentNullException.ThrowIfNull(childTask);

        if (parentTask.Id != 0 && parentTask.Id == childTask.Id)
        {
            throw new InvalidOperationException("Задача не может являться подзадачей самой себя.");
        }

        // Проверка: если parentTask уже является подзадачей (прямой или косвенной) для childTask
        if (IsDescendant(childTask, parentTask))
        {
            throw new InvalidOperationException("Запрещена циклическая вложенность подзадач друг в друга.");
        }

        // Проверка: цепочка навигации вверх через ParentTask
        var currentParent = parentTask;
        while (currentParent != null)
        {
            if (childTask.Id != 0 && currentParent.Id == childTask.Id)
            {
                throw new InvalidOperationException("Запрещена циклическая вложенность подзадач друг в друга.");
            }

            currentParent = currentParent.ParentTask;
        }

        // Проверка: приоритет подзадачи не может быть выше приоритета родительской задачи
        if (parentTask.Priority != null && childTask.Priority != null)
        {
            if (childTask.Priority.Order < parentTask.Priority.Order)
            {
                throw new InvalidOperationException("Подзадача: приоритет не может быть выше приоритета родительской задачи.");
            }
        }

        // Проверка: даты назначения должны совпадать, а дата подзадачи не может быть позже родительской задачи
        if (parentTask.ScheduledDate.HasValue && childTask.ScheduledDate.HasValue)
        {
            if (childTask.ScheduledDate.Value.Date != parentTask.ScheduledDate.Value.Date || childTask.ScheduledDate.Value.Date > parentTask.ScheduledDate.Value.Date)
            {
                throw new InvalidOperationException("Подзадача: даты назначения должны совпадать, а дата не может быть позже родительской задачи.");
            }
        }
    }

    private static bool IsDescendant(TaskItem root, TaskItem target)
    {
        if (root.Subtasks.Count == 0) return false;
        foreach (var sub in root.Subtasks)
        {
            if (sub.Id != 0 && target.Id != 0 && sub.Id == target.Id) return true;
            if (IsDescendant(sub, target)) return true;
        }
        return false;
    }
}
