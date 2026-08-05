using FlowFocus.Core;
using FlowFocus.Core.Models;
using static FlowFocus.Blazor.EditDialogContents.Validators.TaskEditValidator;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class SubtaskHierarchyValidator
{
    public static ValidationResult ValidateSubtasks(
        TaskItem task,
        IEnumerable<SubtaskDto>? subtasks,
        List<PriorityLevel> priorities,
        ITaskRepository taskRepo)
    {
        List<string> errors = [];

        var parentTask = task.ParentTask;
        if (parentTask == null && task.ParentTaskId is > 0)
        {
            parentTask = taskRepo.GetById(task.ParentTaskId.Value);
        }

        if (parentTask != null)
        {
            try
            {
                var taskPriority = task.Priority ?? (task.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == task.PriorityId) : null);
                var parentPriority = parentTask.Priority ?? (parentTask.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == parentTask.PriorityId) : null);

                var taskWrapper = new TaskItem
                {
                    Id = task.Id,
                    Title = task.Title,
                    PriorityId = task.PriorityId,
                    Priority = taskPriority,
                    ScheduledDate = task.ScheduledDate,
                    ParentTaskId = task.ParentTaskId
                };

                var parentWrapper = new TaskItem
                {
                    Id = parentTask.Id,
                    Title = parentTask.Title,
                    PriorityId = parentTask.PriorityId,
                    Priority = parentPriority,
                    ScheduledDate = parentTask.ScheduledDate,
                    ParentTask = parentTask.ParentTask
                };

                Core.Validation.TaskHierarchyValidator.ValidateSubtaskParent(parentWrapper, taskWrapper);
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(ex.Message);
            }
        }

        if (task.Id <= 0) return new(errors.Count == 0, errors);
        var existingTracked = taskRepo.GetById(task.Id);
        if (existingTracked?.Subtasks != null)
        {
            var taskPriority = task.Priority ?? (task.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == task.PriorityId) : null);
            var parentWrapper = new TaskItem
            {
                Id = task.Id,
                Title = task.Title,
                PriorityId = task.PriorityId,
                Priority = taskPriority,
                ScheduledDate = task.ScheduledDate
            };

            foreach (var sub in existingTracked.Subtasks)
            {
                if (subtasks is not null && subtasks.Any(s => s.Id == sub.Id && s.IsDeleted))
                    continue;

                var subPriority = sub.Priority ?? (sub.PriorityId.HasValue ? priorities.FirstOrDefault(p => p.Id == sub.PriorityId) : null);
                var subWrapper = new TaskItem
                {
                    Id = sub.Id,
                    Title = sub.Title,
                    PriorityId = sub.PriorityId,
                    Priority = subPriority,
                    ScheduledDate = sub.ScheduledDate,
                    ParentTaskId = sub.ParentTaskId
                };

                try
                {
                    Core.Validation.TaskHierarchyValidator.ValidateSubtaskParent(parentWrapper, subWrapper);
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"Подзадача \"{sub.Title}\": {ex.Message}");
                }
            }
        }

        return new(errors.Count == 0, errors);
    }
}
