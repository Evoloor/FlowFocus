using FlowFocus.Core.Enums;

namespace FlowFocus.Core.Models;

/// <summary>
/// Частотное распределение задач по источникам дат (Назначение дат)
/// </summary>
public class DateSourceDistribution : BaseEnumDistribution<DateSource>
{
    public DateSourceDistribution() { }

    public DateSourceDistribution(IDictionary<DateSource, int> dictionary)
        : base(dictionary) { }
}
