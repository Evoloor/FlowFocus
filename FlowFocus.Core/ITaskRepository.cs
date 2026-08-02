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
    List<TaskItem> GetTodayTasks();

    /// <summary>Получить задачи на завтра</summary>
    List<TaskItem> GetTomorrowTasks();

    /// <summary>Получить ненастроенные задачи</summary>
    List<TaskItem> GetNotConfiguredTasks();

    /// <summary>Получить количество ненастроенных задач</summary>
    int GetNotConfiguredCount();

    /// <summary>Получить просроченные задачи</summary>
    List<TaskItem> GetOverdueTasks();

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

    /// <summary>Применить повышение приоритета к задаче</summary>
    void ApplyPriorityEscalation(int taskId, int targetPriorityId, IEnumerable<int> appliedEscalationIds, bool saveChanges = true);

    /// <summary>Нормализовать источники дат у задач без даты</summary>
    void NormalizeTaskDateSources(bool saveChanges = true);

    /// <summary>Нормализовать приоритеты блокирующих задач (повысить до уровня заблокированных)</summary>
    void NormalizeBlockingTaskPriorities(bool saveChanges = true);

    /// <summary>Обновить расписание и источник даты задачи</summary>
    void UpdateTaskSchedule(int taskId, DateTime? scheduledDate, FlowFocus.Core.Enums.DateSource? dateSource = null, bool saveChanges = true);

    /// <summary>Обновить статус задачи</summary>
    void UpdateTaskStatus(int taskId, FlowFocus.Core.Enums.TaskStatus status, bool saveChanges = true);

    /// <summary>Получить повторяющиеся задачи-кандидаты для обработки планировщиком</summary>
    List<TaskItem> GetRecurringCandidatesForPlanner();

    /// <summary>Мутировать повторяющуюся задачу на месте (Scenario B)</summary>
    void MutateRecurringTaskInPlace(int taskId, DateTime assignedDate);

    /// <summary>Сохранить пакетные изменения и уведомить UI 1 раз</summary>
    void SaveChangesAndNotify();
}