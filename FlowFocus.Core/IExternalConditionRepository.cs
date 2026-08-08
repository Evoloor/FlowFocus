using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Репозиторий внешних условий
/// </summary>
public interface IExternalConditionRepository : IRepository<ExternalCondition>
{
    /// <summary>Найти условие по имени</summary>
    ExternalCondition? GetByName(string name);

    /// <summary>Получить или создать условие</summary>
    ExternalCondition GetOrCreate(string name);

    /// <summary>Найти условия по части имени</summary>
    List<ExternalCondition> SearchByName(string query, int limit = 10);

    /// <summary>Переключить флаг активности условия</summary>
    void ToggleConditionActive(int conditionId, bool isActive);

    /// <summary>Удалить условие из Настроек с разблокировкой заблокированных им задач</summary>
    void DeleteCondition(int conditionId);

    /// <summary>Увеличить статистику использования</summary>
    void IncrementUsage(int conditionId);

    /// <summary>Уменьшить статистику использования (без авто-удаления из БД)</summary>
    void DecrementUsage(int conditionId);
}
