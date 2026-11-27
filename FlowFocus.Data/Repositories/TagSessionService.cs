using FlowFocus.Core;
using FlowFocus.Core.Models;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Сессионный сервис тегов и внешних условий
/// </summary>
public class TagSessionService(
    ITagRepository tagRepository,
    IExternalConditionRepository? conditionRepository = null) : ITagSessionService
{
    public Tag? LastUsedTag { get; private set; }
    public ExternalCondition? LastUsedCondition { get; private set; }

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
                    LastUsedTag = null;
                }
            }
            catch
            {
                LastUsedTag = null;
            }
        }
        else
        {
            var latestTag = tagRepository.GetAll()
                .Where(t => t.UsageCount > 0 && t.LastUsedDate.HasValue)
                .OrderByDescending(t => t.LastUsedDate)
                .FirstOrDefault();

            if (latestTag != null)
            {
                result.Add(latestTag);
            }
        }

        // Затем заполняем оставшиеся слоты самыми используемыми тегами
        var remainingCount = count - result.Count;
        if (remainingCount <= 0) return result;
        var popular = tagRepository.GetPopularTags(remainingCount + 5); // Берем больше, чтобы исключить уже добавленные
        foreach (var tag in popular.TakeWhile(_ => result.Count < count).Where(tag => result.All(t => t.Id != tag.Id)))
        {
            result.Add(tag);
        }

        return result;
    }

    public void MarkConditionUsed(ExternalCondition condition)
    {
        LastUsedCondition = condition;
        conditionRepository?.IncrementUsage(condition.Id);
    }

    public List<ExternalCondition> GetSuggestedConditions(int count = 5)
    {
        if (conditionRepository == null || count <= 0)
            return [];

        var allConditions = conditionRepository.GetAll();
        List<ExternalCondition> result = [];

        if (LastUsedCondition != null)
        {
            var existing = allConditions.FirstOrDefault(c => c.Id == LastUsedCondition.Id);
            if (existing != null)
            {
                result.Add(existing);
            }
            else
            {
                LastUsedCondition = null;
            }
        }
        else
        {
            var latestCondition = allConditions
                .Where(c => c.LastUsedDate.HasValue)
                .OrderByDescending(c => c.LastUsedDate)
                .FirstOrDefault();

            if (latestCondition != null)
            {
                result.Add(latestCondition);
            }
        }

        var popular = conditionRepository.GetPopularConditions(count + 5);

        foreach (var condition in popular.TakeWhile(_ => result.Count < count).Where(c => result.All(r => r.Id != c.Id)))
        {
            result.Add(condition);
        }

        return result;
    }
}