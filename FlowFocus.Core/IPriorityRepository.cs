using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Репозиторий приоритетов
/// </summary>
public interface IPriorityRepository : IRepository<PriorityLevel>
{
    /// <summary>Получить все приоритеты отсортированные по Order</summary>
    List<PriorityLevel> GetAllOrdered();

    /// <summary>Получить самый важный (критический) приоритет</summary>
    PriorityLevel? GetHighestPriority();

    /// <summary>Получить приоритеты важнее указанного</summary>
    List<PriorityLevel> GetPrioritiesHigherThan(int priorityId);

    /// <summary>Переупорядочить приоритеты</summary>
    void Reorder(List<int> orderedIds);
}