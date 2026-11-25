namespace FlowFocus.Core.Models;

/// <summary>
/// Сравнение одной метрики между текущим и предыдущим 7-дневными периодами
/// </summary>
public class MetricPeriodComparison
{
    public double? CurrentValue { get; set; }
    public double? PreviousValue { get; set; }

    public double? AbsoluteDelta =>
        CurrentValue.HasValue && PreviousValue.HasValue
            ? Math.Round(CurrentValue.Value - PreviousValue.Value, 1)
            : null;

    public double? PercentDelta =>
        CurrentValue.HasValue && PreviousValue.HasValue && PreviousValue.Value > 0
            ? Math.Round((CurrentValue.Value - PreviousValue.Value) / PreviousValue.Value * 100.0, 1)
            : null;

    public bool HasAnyData => CurrentValue.HasValue || PreviousValue.HasValue;
}

/// <summary>
/// Сводка сравнения выполненных задач за последние 7 туду-дней против предыдущих 7 туду-дней
/// </summary>
public class WeeklyComparisonDto
{
    public MetricPeriodComparison CompletedTasksCount { get; set; } = new();
    public MetricPeriodComparison CompletedTimeMinutes { get; set; } = new();
    public MetricPeriodComparison AverageComplexity { get; set; } = new();
    public MetricPeriodComparison AverageInterest { get; set; } = new();

    public bool HasAnyData =>
        (CompletedTasksCount.CurrentValue ?? 0) > 0 || (CompletedTasksCount.PreviousValue ?? 0) > 0;
}
