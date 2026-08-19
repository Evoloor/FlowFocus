using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

/// <summary>
/// Сервис нормализации связей между задачами:
/// выявляет и очищает некорректные самоблокировки (SourceTaskId == TargetTaskId)
/// и добавляет к затронутым задачам тег «проверить_блокер».
/// </summary>
public static class RelationNormalizer
{
    public const string BrokenBlockerTagName = "проверить_блокер";

    public static bool NormalizeTaskRelations(StorageContext context, TagRepository tagRepository)
    {
        var selfRelations = context.TaskRelations
            .Where(r => r.SourceTaskId > 0 && r.SourceTaskId == r.TargetTaskId)
            .ToList();

        if (selfRelations.Count == 0)
        {
            return false;
        }

        var affectedTaskIds = selfRelations.Select(r => r.SourceTaskId).Distinct().ToList();

        // Находим или создаем тег "проверить_блокер"
        var tag = tagRepository.GetOrCreate(BrokenBlockerTagName);

        var affectedTasks = context.Tasks
            .Include(t => t.Tags)
            .Include(t => t.InverseRelations)
            .Include(t => t.Conditions).ThenInclude(tc => tc.Condition)
            .Where(t => affectedTaskIds.Contains(t.Id))
            .ToList();

        foreach (var task in affectedTasks)
        {
            // Добавляем тег, если его ещё нет
            if (!task.Tags.Any(tt => tt.TagId == tag.Id))
            {
                context.TaskTags.Add(new TaskTag
                {
                    TaskId = task.Id,
                    TagId = tag.Id
                });
                tag.UsageCount++;
                tag.LastUsedDate = DateTime.UtcNow;
            }

            // Если задача находилась в статусе Blocked только из-за самоблокировки, меняем статус
            if (task.Status == TaskStatus.Blocked)
            {
                task.Status = TaskStatus.Planned;
            }

            task.LastChangesOn = DateTime.UtcNow;
        }

        // Удаляем все записи самоблокировок
        context.TaskRelations.RemoveRange(selfRelations);

        return true;
    }
}
