using FlowFocus.Core.Models;

namespace FlowFocus.Blazor.Dialogs;

public class RelationDto
{
    public int? Id { get; set; }
    public FlowFocus.Core.Enums.RelationType Type { get; set; }
    public TaskItem? TargetTask { get; set; }
}
