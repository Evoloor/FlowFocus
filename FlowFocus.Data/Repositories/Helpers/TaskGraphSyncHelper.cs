using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Repositories.Helpers;

/// <summary>
/// Хелпер синхронизации дочерних графов и навигационных свойств сущности TaskItem
/// </summary>
public static class TaskGraphSyncHelper
{
    #region Add Helpers

    public static void PrepareSubtasksForAdd(TaskItem entity)
    {
        foreach (var subtask in entity.Subtasks)
        {
            subtask.ParentTaskId ??= entity.Id;
            if (subtask.CreatedDate == default)
            {
                subtask.CreatedDate = DateTime.UtcNow;
            }
            subtask.Status = TaskStatus.Planned;
        }
    }

    public static void PrepareRelationsForAdd(TaskItem entity)
    {
        for (var i = 0; i < entity.Relations.Count; i++)
        {
            var relation = entity.Relations[i];
            
            if (relation.SourceTaskId == 0)
            {
                relation.SourceTaskId = entity.Id;
            }

            if (relation.TargetTaskId == 0)
            {
                entity.Relations[i] = new TaskRelation
                {
                    Id = relation.Id > 0 ? relation.Id : 0,
                    SourceTaskId = relation.SourceTaskId == 0 ? entity.Id : relation.SourceTaskId,
                    TargetTaskId = entity.Id,
                    Type = relation.Type,
                    LastChangesOn = DateTime.UtcNow
                };
            }
            else
            {
                relation.LastChangesOn = DateTime.UtcNow;
            }
        }
    }

    public static void PrepareEscalationsForAdd(TaskItem entity)
    {
        foreach (var escalation in entity.PriorityEscalations)
        {
            if (escalation.TaskId == 0)
            {
                escalation.TaskId = entity.Id;
            }
            escalation.LastChangesOn = DateTime.UtcNow;
        }
    }

    #endregion

    #region Update Helpers

    public static List<int> UpdateTags(StorageContext context, TaskItem tracked, TaskItem source)
    {
        var sourceTagIds = source.Tags.Select(st => st.TagId).ToHashSet();
        var trackedTagIds = tracked.Tags.Select(tt => tt.TagId).ToHashSet();

        var tagsToRemove = tracked.Tags.Where(tt => !sourceTagIds.Contains(tt.TagId)).ToList();
        var removedTagIds = tagsToRemove.Select(tt => tt.TagId).ToList();

        foreach (var tag in tagsToRemove)
        {
            context.TaskTags.Remove(tag);
        }

        foreach (var sourceTag in source.Tags.Where(st => !trackedTagIds.Contains(st.TagId)))
        {
            context.TaskTags.Add(new TaskTag
            {
                TaskId = tracked.Id,
                TagId = sourceTag.TagId
            });
        }

        return removedTagIds;
    }

    public static void UpdateSubtasks(StorageContext context, TaskItem tracked, TaskItem source)
    {
        var subtasksToRemove = tracked.Subtasks
            .Where(st => !source.Subtasks.Any(sst => sst.Id == st.Id && sst.Id > 0))
            .ToList();

        foreach (var subtask in subtasksToRemove)
        {
            context.Tasks.Remove(subtask);
        }

        foreach (var sourceSubtask in source.Subtasks)
        {
            if (sourceSubtask.Id > 0)
            {
                var existing = tracked.Subtasks.FirstOrDefault(s => s.Id == sourceSubtask.Id);
                if (existing != null)
                {
                    context.Entry(existing).CurrentValues.SetValues(sourceSubtask);
                }
            }
            else
            {
                sourceSubtask.ParentTaskId = tracked.Id;
                context.Tasks.Add(sourceSubtask);
            }
        }
    }

    public static void UpdateRelations(StorageContext context, TaskItem tracked, TaskItem source)
    {
        var desired = source.Relations ?? [];
        var trackedAll = (tracked.Relations ?? []).Concat(tracked.InverseRelations ?? []).ToList();

        var toRemove = trackedAll.Where(r =>
            !((r.Id > 0 && desired.Any(d => d.Id > 0 && d.Id == r.Id)) ||
              desired.Any(d => d.SourceTaskId == r.SourceTaskId && d.TargetTaskId == r.TargetTaskId && d.Type == r.Type))
        ).ToList();

        foreach (var rel in toRemove)
        {
            context.TaskRelations.Remove(rel);
        }

        foreach (var desiredRel in desired)
        {
            TaskRelation? existing = null;

            if (desiredRel.Id > 0)
            {
                existing = trackedAll.FirstOrDefault(r => r.Id == desiredRel.Id);
            }

            existing ??= trackedAll.FirstOrDefault(r => 
                r.SourceTaskId == desiredRel.SourceTaskId && 
                r.TargetTaskId == desiredRel.TargetTaskId && 
                r.Type == desiredRel.Type);

            if (existing != null)
            {
                context.Entry(existing).CurrentValues.SetValues(desiredRel);
            }
            else
            {
                if (desiredRel.SourceTaskId == 0)
                {
                    desiredRel.SourceTaskId = tracked.Id;
                }
                context.TaskRelations.Add(desiredRel);
            }
        }
    }

    public static void UpdateEscalations(StorageContext context, TaskItem tracked, TaskItem source)
    {
        var escalationsToRemove = tracked.PriorityEscalations
            .Where(e => !source.PriorityEscalations.Any(se => se.Id == e.Id && se.Id > 0))
            .ToList();

        foreach (var escalation in escalationsToRemove)
        {
            context.PriorityEscalations.Remove(escalation);
        }

        foreach (var sourceEscalation in source.PriorityEscalations)
        {
            if (sourceEscalation.Id > 0)
            {
                var existing = tracked.PriorityEscalations.FirstOrDefault(e => e.Id == sourceEscalation.Id);
                if (existing != null)
                {
                    context.Entry(existing).CurrentValues.SetValues(sourceEscalation);
                }
            }
            else
            {
                sourceEscalation.TaskId = tracked.Id;
                context.PriorityEscalations.Add(sourceEscalation);
            }
        }
    }

    #endregion
}
