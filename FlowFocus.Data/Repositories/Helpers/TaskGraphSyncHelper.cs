using FlowFocus.Core.Models;
using FlowFocus.Core.Validation;
using Microsoft.EntityFrameworkCore;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Repositories.Helpers;

/// <summary>
/// Хелпер синхронизации дочерних графов и навигационных свойств сущности TaskItem
/// </summary>
public static class TaskGraphSyncHelper
{
    #region Add Helpers

    public static void PrepareSubtasksForAdd(StorageContext context, TaskItem entity)
    {
        var dbMax = context.Tasks.AsNoTracking().Select(t => (int?)t.Id).Max() ?? 0;
        var maxId = Math.Max(dbMax, entity.Id);
        foreach (var subtask in entity.Subtasks)
        {
            subtask.ParentTaskId ??= entity.Id;
            if (subtask.Id == 0)
            {
                maxId++;
                subtask.Id = maxId;
            }
            TaskHierarchyValidator.ValidateSubtaskParent(entity, subtask);
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

    public static List<int> UpdateConditions(StorageContext context, TaskItem tracked, TaskItem source)
    {
        var sourceConditionIds = source.Conditions.Select(sc => sc.ConditionId).ToHashSet();
        var trackedConditionIds = tracked.Conditions.Select(tc => tc.ConditionId).ToHashSet();

        var conditionsToRemove = tracked.Conditions.Where(tc => !sourceConditionIds.Contains(tc.ConditionId)).ToList();
        var removedConditionIds = conditionsToRemove.Select(tc => tc.ConditionId).ToList();

        foreach (var condition in conditionsToRemove)
        {
            context.TaskConditions.Remove(condition);
        }

        foreach (var sourceCondition in source.Conditions.Where(sc => !trackedConditionIds.Contains(sc.ConditionId)))
        {
            context.TaskConditions.Add(new TaskCondition
            {
                TaskId = tracked.Id,
                ConditionId = sourceCondition.ConditionId
            });
        }

        return removedConditionIds;
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
            sourceSubtask.ParentTaskId ??= tracked.Id;
            TaskHierarchyValidator.ValidateSubtaskParent(tracked, sourceSubtask);

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
        var trackedOutgoing = tracked.Relations ?? [];
        var trackedIncoming = tracked.InverseRelations ?? [];

        var toRemoveOutgoing = trackedOutgoing.Where(r =>
            !((r.Id > 0 && desired.Any(d => d.Id > 0 && d.Id == r.Id)) ||
              desired.Any(d => d.SourceTaskId == r.SourceTaskId && d.TargetTaskId == r.TargetTaskId && d.Type == r.Type))
        ).ToList();

        foreach (var rel in toRemoveOutgoing)
        {
            context.TaskRelations.Remove(rel);
        }

        var toRemoveIncoming = trackedIncoming.Where(r =>
            r.Id > 0 && !desired.Any(d => d.Id > 0 && d.Id == r.Id)
        ).ToList();

        foreach (var rel in toRemoveIncoming)
        {
            context.TaskRelations.Remove(rel);
        }

        foreach (var desiredRel in desired)
        {
            TaskRelation? existing = null;

            if (desiredRel.Id > 0)
            {
                existing = trackedOutgoing.FirstOrDefault(r => r.Id == desiredRel.Id)
                           ?? trackedIncoming.FirstOrDefault(r => r.Id == desiredRel.Id)
                           ?? context.TaskRelations.Local.FirstOrDefault(r => r.Id == desiredRel.Id);
            }

            existing ??= trackedOutgoing.FirstOrDefault(r => 
                r.SourceTaskId == desiredRel.SourceTaskId && 
                r.TargetTaskId == desiredRel.TargetTaskId && 
                r.Type == desiredRel.Type)
                ?? trackedIncoming.FirstOrDefault(r =>
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
