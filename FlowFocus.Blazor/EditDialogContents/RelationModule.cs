using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents;

public static class RelationModule
{
    public static List<TaskRelation> SyncRelationsToTask(List<RelationDto> dtos, TaskItem task, TaskItem? existingTask)
    {
        var validRelations = dtos.Where(r => r.TargetTask != null).ToList();
        var existingRelationsSource = existingTask?.Relations ?? [];
        var resultRelations = new List<TaskRelation>();

        foreach (var dto in validRelations)
        {
            if (dto.Id is > 0)
            {
                var existing = existingRelationsSource.FirstOrDefault(r => r.Id == dto.Id.Value);
                if (existing != null)
                {
                    var updatedRelation = new TaskRelation
                    {
                        Id = existing.Id,
                        SourceTaskId = task.Id > 0 ? task.Id : existing.SourceTaskId,
                        TargetTaskId = dto.TargetTask!.Id,
                        Type = dto.Type,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultRelations.Add(updatedRelation);
                }
                else
                {
                    var newRelation = new TaskRelation
                    {
                        SourceTaskId = task.Id > 0 ? task.Id : 0,
                        TargetTaskId = dto.TargetTask!.Id,
                        Type = dto.Type,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultRelations.Add(newRelation);
                }
            }
            else
            {
                var newRelation = new TaskRelation
                {
                    SourceTaskId = task.Id > 0 ? task.Id : 0,
                    TargetTaskId = dto.TargetTask!.Id,
                    Type = dto.Type,
                    LastChangesOn = DateTime.UtcNow
                };
                resultRelations.Add(newRelation);
            }
        }

        return resultRelations;
    }
}