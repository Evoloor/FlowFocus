using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Репозиторий тегов
/// </summary>
public interface ITagRepository : IRepository<Tag>
{
    /// <summary>Найти тег по имени</summary>
    Tag? GetByName(string name);

    /// <summary>Получить или создать тег</summary>
    Tag GetOrCreate(string name);

    /// <summary>Получить популярные теги</summary>
    List<Tag> GetPopularTags(int count);

    /// <summary>Найти теги по части имени</summary>
    List<Tag> SearchByName(string query, int limit = 10);

    /// <summary>Обновить статистику использования тега</summary>
    void IncrementUsage(int tagId);

    /// <summary>Уменьшить статистику использования тега (при удалении из задачи)</summary>
    void DecrementUsage(int tagId);
    
    /// <summary>Проверить и удалить неиспользуемые теги</summary>
    void CleanupUnusedTags(List<int> tagIds);
}