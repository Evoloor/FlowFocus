using FlowFocus.Core.Models;

namespace FlowFocus.Core.Services;

/// <summary>
/// Сервис расчета и управления логикой повторения задач
/// </summary>
public interface ITaskRecurrenceService
{
    /// <summary>
    /// Расчитать следующую дату повторения задачи
    /// </summary>
    DateTime? CalculateNextRecurrenceDate(TaskItem task);

    /// <summary>
    /// Создать следующий экземпляр повторяющейся задачи, если это необходимо
    /// </summary>
    void HandleTaskCompletionRecurrence(TaskItem sourceTask, Func<int, DateTime?, DateTime?, bool> existsPredicate, Action<TaskItem> onNewTaskCreated);
}
