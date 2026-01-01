using System;

namespace FlowFocus.Blazor.Dialogs;

public class EscalationDto
{
    public int? Id { get; set; }
    public int TargetPriorityId { get; set; }
    public DateTime? EscalationDate { get; set; }
}
