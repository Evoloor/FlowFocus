using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Сервис алгоритмического планирования
/// </summary>
public interface IPlannerService
{
    /// <summary>Пересчитать приоритеты на основе таблиц повышения</summary>
    void ActualizePriorities();

    /// <summary>Нормализовать приоритеты блокеров</summary>
    void NormalizeBlockerPriorities();

    /// <summary>Распределить задачи по дням</summary>
    void DistributeTasks(UserSettings settings);

    /// <summary>Полный пересчёт: актуализация приоритетов + распределение</summary>
    void RecalculateAll(UserSettings settings);

    /// <summary>Проверить и обновить статусы заблокированных задач</summary>
    void UpdateBlockedStatuses();

    /// <summary>
    /// Отложить задачу из сегодняшнего плана
    /// </summary>
    /// <param name="taskId">ID задачи</param>
    /// <param name="target">Цель переноса (Авто или Строго на завтра)</param>
    /// <param name="replaceWithBacklog">Заменить на другую задачу из бэклога (сохранить лимит) или разгрузить день (зафиксировать план дня)</param>
    /// <param name="settings">Настройки пользователя (если null, будут получены из репозитория)</param>
    void PostponeTask(int taskId, PostponeTarget target, bool replaceWithBacklog, UserSettings? settings = null);

    /// <summary>
    /// Установить или снять фиксацию сегодняшнего плана
    /// </summary>
    void SetTodayPlanLocked(bool isLocked, UserSettings? settings = null);
}