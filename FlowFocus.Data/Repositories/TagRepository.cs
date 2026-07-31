using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FlowFocus.Data.Repositories;

/// <summary>
/// Репозиторий тегов
/// </summary>
public class TagRepository(StorageContext context, INotificationService notificationService) : CachedRepository<Tag>(context, notificationService), ITagRepository
{
    protected override DbSet<Tag> GetDbSet() => Context.Tags;

    public Tag? GetByName(string name)
    {
        return GetAll().FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Tag GetOrCreate(string name)
    {
        var existing = GetByName(name);
        if (existing != null) return existing;

        Tag tag = new()
        {
            Name = name,
            BackgroundColor = GeneratePastelColor()
        };
        Add(tag);
        return tag;
    }

    public List<Tag> GetPopularTags(int count)
    {
        return GetAll()
            .OrderByDescending(t => t.UsageCount)
            .Take(count)
            .ToList();
    }

    public List<Tag> SearchByName(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        return GetAll()
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.UsageCount)
            .Take(limit)
            .ToList();
    }

    public void IncrementUsage(int tagId)
    {
        UpdatePartial(tagId, tag =>
        {
            tag.UsageCount++;
            tag.LastUsedDate = DateTime.UtcNow;
        });
    }
    
    public void CleanupUnusedTags(List<int> tagIds)
    {
        if (tagIds == null || tagIds.Count == 0) return;
        
        lock (CacheLock)
        {
            foreach (var tag in tagIds
                         .Select(tagId => new { tagId, hasReferences = Context.TaskTags.Any(tt => tt.TagId == tagId) })
                         .Where(t => !t.hasReferences)
                         .Select(t => Context.Tags.Find(t.tagId)).OfType<Tag>())
            {
                Context.Tags.Remove(tag);
            }
            
            Context.SaveChanges();
            MarkDirty();
        }
    }

    public void DecrementUsage(int tagId)
    {
        // Атомарно уменьшаем UsageCount и удаляем тег, если он больше не используется
        if (tagId <= 0) return;

        lock (CacheLock)
        {
            var tag = Context.Tags.Find(tagId);
            if (tag == null) return;

            tag.UsageCount = Math.Max(0, tag.UsageCount - 1);

            // Если usageCount уменьшился до 0, и нет ссылок в TaskTags — удалим тег из базы
            var hasReferences = Context.TaskTags.Any(tt => tt.TagId == tagId);
            if (tag.UsageCount == 0 && !hasReferences)
            {
                Context.Tags.Remove(tag);
            }

            Context.SaveChanges();
            MarkDirty();
        }
    }
    
    private static readonly string[] PastelColors =
    [
        "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9", "#BAE1FF",
        "#E0BBE4", "#FEC8D8", "#D4F0F0", "#CCE2CB", "#B6CFB6",
        "#97C1A9", "#FCB9AA", "#FFDBCC", "#ECEAE4", "#A2E1DB"
    ];

    private static string GeneratePastelColor()
    {
        return PastelColors[Random.Shared.Next(PastelColors.Length)];
    }
}