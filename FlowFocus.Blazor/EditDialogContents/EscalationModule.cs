using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents;

public static class EscalationModule
{
    // Move SyncEscalationsToTask logic here so TaskEditDialog can call it
    public static List<PriorityEscalation> SyncEscalationsToTask(List<EscalationDto> dtos, TaskItem task, TaskItem? existingTask, List<PriorityLevel> priorities)
    {
        var existingEscalationsSource = existingTask?.PriorityEscalations ?? [];
        var resultEscalations = new List<PriorityEscalation>();

        foreach (var dto in dtos)
        {
            if (dto.Id is > 0)
            {
                var existing = existingEscalationsSource.FirstOrDefault(e => e.Id == dto.Id.Value);
                if (existing != null)
                {
                    var updatedEscalation = new PriorityEscalation
                    {
                        Id = existing.Id,
                        TaskId = task.Id > 0 ? task.Id : existing.TaskId,
                        TargetPriorityId = dto.TargetPriorityId,
                        EscalationDate = dto.EscalationDate ?? DateTime.Today,
                        IsApplied = existing.IsApplied,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultEscalations.Add(updatedEscalation);
                }
                else
                {
                    var newEscalation = new PriorityEscalation
                    {
                        TaskId = task.Id > 0 ? task.Id : 0,
                        TargetPriorityId = dto.TargetPriorityId,
                        EscalationDate = dto.EscalationDate ?? DateTime.Today,
                        IsApplied = false,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultEscalations.Add(newEscalation);
                }
            }
            else
            {
                var newEscalation = new PriorityEscalation
                {
                    TaskId = task.Id > 0 ? task.Id : 0,
                    TargetPriorityId = dto.TargetPriorityId,
                    EscalationDate = dto.EscalationDate ?? DateTime.Today,
                    IsApplied = false,
                    LastChangesOn = DateTime.UtcNow
                };
                resultEscalations.Add(newEscalation);
            }
        }

        return resultEscalations;
    }
}