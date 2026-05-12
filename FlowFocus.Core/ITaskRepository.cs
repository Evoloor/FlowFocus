using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Репозиторий задач с расширенными методами фильтрации
/// </summary>
public interface ITaskRepository : IRepository<TaskItem>
{
    /// <summary>Получить задачи на указанную дату</summary>
    List<TaskItem> GetTasksForDate(DateTime date);

    /// <summary>Получить задачи на сегодня</summary>
    List<TaskItem> GetTodayTasks(int dayStartHour);

    /// <summary>Получить задачи на завтра</summary>
    List<TaskItem> GetTomorrowTasks();

    /// <summary>Получить ненастроенные задачи</summary>
    List<TaskItem> GetNotConfiguredTasks();

    /// <summary>Получить количество ненастроенных задач</summary>
    int GetNotConfiguredCount();

    /// <summary>Получить просроченные задачи</summary>
    List<TaskItem> GetOverdueTasks(int dayStartHour);

    /// <summary>Получить задачи, которые разблокируются при выполнении указанной задачи</summary>
    List<TaskItem> GetTasksUnblockedBy(int taskId);

    /// <summary>Получить все задачи для автодополнения (без подзадач)</summary>
    List<TaskItem> GetTasksForAutocomplete();

    /// <summary>Пометить задачу как выполненную</summary>
    void CompleteTask(int taskId);

    /// <summary>Отменить пометку о выполнении (сделать незавершённой)</summary>
    void ReopenTask(int taskId);

    /// <summary>Пометить задачу как неактуальную</summary>
    void MarkIrrelevant(int taskId);

    /// <summary>Вернуть задачу из неактуальных в актуальные</summary>
    void RestoreFromIrrelevant(int taskId);

    /// <summary>Получить интересную задачу для прокрастинации</summary>
    TaskItem? GetProcrastinationTask(List<int> excludeIds);

    /// <summary>Получить наименее приоритетную задачу дня</summary>
    TaskItem? GetLeastPriorityTaskOfDay();

    /// <summary>Удалить запись о связи между задачами</summary>
    void DeleteRelation(int relationId);
}