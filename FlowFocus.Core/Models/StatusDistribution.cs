using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

/// <summary>
/// Частотное распределение задач по статусам
/// </summary>
public class StatusDistribution : BaseEnumDistribution<TaskStatus>
{
    public StatusDistribution() { }

    public StatusDistribution(IDictionary<TaskStatus, int> dictionary)
        : base(dictionary) { }
}
