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
}