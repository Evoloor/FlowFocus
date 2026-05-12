namespace FlowFocus.Core.Models;

public interface IAuditEntity
{
    int Id { get; set; }
    DateTime LastChangesOn { get; set; }
}