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

        if (task.Id <= 0) return new(errors.Count == 0, errors);

        var existingTracked = taskRepo.GetById(task.Id);
        if (existingTracked?.Subtasks != null)
        {
            foreach (var sub in existingTracked.Subtasks)
            {
                if (subtasks is not null && subtasks.Any(s => s.Id == sub.Id && s.IsDeleted))
                    continue;

                try
                {
                    Core.Validation.TaskHierarchyValidator.ValidateSubtaskParent(task, sub);
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
