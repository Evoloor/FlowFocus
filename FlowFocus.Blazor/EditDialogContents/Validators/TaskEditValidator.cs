using FlowFocus.Core;
using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.EditDialogContents.Validators;

public static class TaskEditValidator
{
    public record ValidationResult(bool IsValid, List<string> Errors);

    public static ValidationResult ValidateEscalations(IEnumerable<EscalationDto>? escalations, TaskItem task,
        List<PriorityLevel> priorities) =>
        EscalationValidator.ValidateEscalations(escalations, task, priorities);

    public static ValidationResult ValidateRelations(IEnumerable<RelationDto>? relations, TaskItem task,
        List<PriorityLevel> priorities) =>
        RelationValidator.ValidateRelations(relations, task, priorities);

    public static ValidationResult ValidateSubtasks(
        TaskItem task,
        IEnumerable<SubtaskDto>? subtasks,
        List<PriorityLevel> priorities,
        ITaskRepository taskRepo) =>
        SubtaskHierarchyValidator.ValidateSubtasks(task, subtasks, priorities, taskRepo);
}