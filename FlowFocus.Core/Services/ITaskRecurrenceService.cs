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
    /// Расчитать следующую дату повторения задачи от указанной базовой даты
    /// </summary>
    DateTime? CalculateNextRecurrenceDateFromBase(TaskItem task, DateTime baseDate);

    /// <summary>
    /// Создать следующий экземпляр повторяющейся задачи, если это необходимо
    /// </summary>
    void HandleTaskCompletionRecurrence(TaskItem sourceTask, Func<int, DateTime?, DateTime?, bool> existsPredicate, Action<TaskItem> onNewTaskCreated);
}
