using FlowFocus.Core;
using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents;

public static class EscalationModule
{
    // Move SyncEscalationsToTask logic here so TaskEditDialog can call it
    public static List<PriorityEscalation> SyncEscalationsToTask(List<EscalationDto> dtos, TaskItem task, TaskItem? existingTask, List<PriorityLevel> priorities)
    {
        var existingEscalationsSource = existingTask?.PriorityEscalations ?? [];
        List<PriorityEscalation> resultEscalations = [];

        foreach (var dto in dtos)
        {
            if (dto.Id is > 0)
            {
                var existing = existingEscalationsSource.FirstOrDefault(e => e.Id == dto.Id.Value);
                if (existing != null)
                {
                    PriorityEscalation updatedEscalation = new()
                    {
                        Id = existing.Id,
                        TaskId = task.Id > 0 ? task.Id : existing.TaskId,
                        TargetPriorityId = dto.TargetPriorityId,
                        EscalationDate = dto.EscalationDate ?? TodoDay.Today.ToDateTime(),
                        IsApplied = existing.IsApplied,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultEscalations.Add(updatedEscalation);
                }
                else
                {
                    PriorityEscalation newEscalation = new()
                    {
                        TaskId = task.Id > 0 ? task.Id : 0,
                        TargetPriorityId = dto.TargetPriorityId,
                        EscalationDate = dto.EscalationDate ?? TodoDay.Today.ToDateTime(),
                        IsApplied = false,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultEscalations.Add(newEscalation);
                }
            }
            else
            {
                PriorityEscalation newEscalation = new()
                {
                    TaskId = task.Id > 0 ? task.Id : 0,
                    TargetPriorityId = dto.TargetPriorityId,
                    EscalationDate = dto.EscalationDate ?? TodoDay.Today.ToDateTime(),
                    IsApplied = false,
                    LastChangesOn = DateTime.UtcNow
                };
                resultEscalations.Add(newEscalation);
            }
        }

        return resultEscalations;
    }
}