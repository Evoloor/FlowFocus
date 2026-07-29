namespace FlowFocus.Core.Enums;

/// <summary>
/// Tracks the origin and behaviour of a task's scheduling date.
/// </summary>
public enum DateSource
{
    /// <summary>
    /// The date was explicitly set by the user via the UI date picker.
    /// Acts as a hard anchor — the planner must not move it.
    /// </summary>
    Manual,

    /// <summary>
    /// The date was assigned by the system according to a recurrence rule (e.g. previousDate + 1 day).
    /// Acts as a fixed anchor — the planner must not move it during normal distribution.
    /// Overdue recurring tasks that are auto-redistributed are mutated in-place and keep this source.
    /// </summary>
    AutoFixed,

    /// <summary>
    /// The date was assigned (or will be assigned) by the auto-distribution engine.
    /// The planner may freely recalculate and move this date.
    /// </summary>
    AutoFlexible
}
