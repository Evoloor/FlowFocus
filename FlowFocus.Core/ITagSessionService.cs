using FlowFocus.Core.Models;

namespace FlowFocus.Core;

/// <summary>
/// Сессионный сервис для тегов (хранит недавно использованные)
/// </summary>
public interface ITagSessionService
{
    /// <summary>Последний использованный тег в сессии</summary>
    Tag? LastUsedTag { get; }

    /// <summary>Отметить тег как использованный</summary>
    void MarkTagUsed(Tag tag);

    /// <summary>Получить рекомендуемые теги (последний + популярные)</summary>
    List<Tag> GetSuggestedTags(int count = 5);

    /// <summary>Последнее использованное условие в сессии</summary>
    ExternalCondition? LastUsedCondition { get; }

    /// <summary>Отметить условие как использованное</summary>
    void MarkConditionUsed(ExternalCondition condition);

    /// <summary>Получить рекомендуемые условия (последнее + популярные)</summary>
    List<ExternalCondition> GetSuggestedConditions(int count = 5);
}