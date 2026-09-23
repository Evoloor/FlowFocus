using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Data.Services;

public class PriorityEscalationPlanner(ITaskRepository taskRepository)
{
    public void ActualizePriorities()
    {
        var today = TodoDay.Today;
        var tasks = taskRepository.GetAll();

        foreach (var task in tasks)
        {
            if (task.IsInactive)
                continue;

            if (task.IsRecurring)
                continue;
            
            var escalations = task.PriorityEscalations
                .Where(e => !e.IsApplied && e.EscalationDate.Date <= today.Date)
                .OrderBy(e => e.TargetPriority?.Order ?? 99)
                .ToList();

            if (escalations.Count == 0) continue;
            var highestEscalation = escalations.First();
                
            taskRepository.ApplyPriorityEscalation(
                task.Id,
                highestEscalation.TargetPriorityId,
                escalations.Select(e => e.Id),
                saveChanges: true
            );
        }

        NormalizeBlockerPriorities();
    }

    public void NormalizeBlockerPriorities()
    {
        var tasks = taskRepository.GetAll()
            .Where(t => t.IsActive)
            .ToList();

        var taskMap = tasks.ToDictionary(t => t.Id);

        bool changed;
        do
        {
            changed = false;
            foreach (var task in tasks)
            {
                var targetOrder = GetEffectivePriorityOrder(task);
                if (!task.PriorityId.HasValue) continue;

                foreach (var relation in task.InverseRelations.Where(r => r.Type == RelationType.Blocks))
                {
                    if (!taskMap.TryGetValue(relation.SourceTaskId, out var blockerTask)) continue;
                    if (blockerTask.IsInactive) continue;

                    var blockerOrder = GetEffectivePriorityOrder(blockerTask);
                    if (targetOrder < blockerOrder)
                    {
                        blockerTask.PriorityId = task.PriorityId.Value;
                        foreach (var pe in blockerTask.PriorityEscalations.Where(e => !e.IsApplied))
                        {
                            var peOrder = pe.TargetPriority?.Order ?? 99;
                            if (peOrder >= targetOrder)
                            {
                                pe.IsApplied = true;
                                pe.LastChangesOn = DateTime.UtcNow;
                            }
                        }
                        taskRepository.Update(blockerTask);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                taskRepository.SaveChanges();
                tasks = taskRepository.GetAll()
                    .Where(t => t.IsActive)
                    .ToList();
                taskMap = tasks.ToDictionary(t => t.Id);
            }
        } while (changed);
    }

    public void UpdateBlockedStatuses()
    {
        var tasks = taskRepository.GetAll();

        foreach (var task in tasks)
        {
            if (task.IsInactive)
                continue;

            var isBlocked = FlowFocus.Core.Helpers.TaskStatusCalculator.IsTaskBlocked(task);

            if (isBlocked && task.Status != TaskStatus.Blocked)
            {
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Blocked, saveChanges: false);
            }
            else if (!isBlocked && task.Status == TaskStatus.Blocked)
            {
                taskRepository.UpdateTaskStatus(task.Id, TaskStatus.Planned, saveChanges: false);
            }
        }
    }

    private static int GetEffectivePriorityOrder(TaskItem task) => task.Priority?.Order ?? 99;
}
