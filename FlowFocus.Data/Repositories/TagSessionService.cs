using FlowFocus.Core;
using FlowFocus.Core.Models;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Сессионный сервис тегов
/// </summary>
public class TagSessionService(ITagRepository tagRepository) : ITagSessionService
{
    public Tag? LastUsedTag { get; private set; }

    public void MarkTagUsed(Tag tag)
    {
        LastUsedTag = tag;
        tagRepository.IncrementUsage(tag.Id);
    }

    public List<Tag> GetSuggestedTags(int count = 5)
    {
        List<Tag> result = [];

        // Сперва последний использованный тег из хипа (за сессию), если такой имеется и он ещё валиден
        if (LastUsedTag != null)
        {
            try
            {
                var existing = tagRepository.GetById(LastUsedTag.Id);
                if (existing is { UsageCount: > 0 })
                {
                    result.Add(existing);
                }
                else
                {
                    // Если тег удалён или больше не актуален — сбросим ссылку
                    LastUsedTag = null;
                }
            }
            catch
            {
                LastUsedTag = null;
            }
        }

        // Затем заполняем ещё 4 (или 5, если не было недавнего) по принципу самых используемых в созданных делах
        var remainingCount = count - result.Count;
        if (remainingCount <= 0) return result;
        var popular = tagRepository.GetPopularTags(remainingCount + 5); // Берем больше, чтобы исключить уже добавленные
        foreach (var tag in popular.TakeWhile(tag => result.Count < count).Where(tag => result.All(t => t.Id != tag.Id)))
        {
            result.Add(tag);
        }

        return result;
    }
}