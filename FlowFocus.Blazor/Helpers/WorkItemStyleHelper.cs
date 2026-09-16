using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Blazor.Helpers;

/// <summary>
/// Единый хелпер для вычисления CSS-классов карточек и заголовков рабочих элементов (TaskItem, SubtaskItem).
/// </summary>
public static class WorkItemStyleHelper
{
    /// <summary>
    /// Возвращает набор CSS-классов карточки на основе состояния рабочего элемента.
    /// </summary>
    public static string GetCardClass(WorkItemBase item, bool isNested = false, DisplayMode displayMode = DisplayMode.List)
    {
        List<string> classes = ["task-card"];

        if (item.Status == TaskStatus.Completed)
            classes.Add("task-card-completed");
        else if (item.Status == TaskStatus.Irrelevant)
            classes.Add("task-card-irrelevant");
        else if (item.Status == TaskStatus.NotConfigured)
            classes.Add("task-card-not-configured");
        else if (item.Status == TaskStatus.Blocked || (item is TaskItem task && task.IsBlocked))
            classes.Add("task-card-blocked");

        if (isNested)
            classes.Add("task-card-nested");

        if (displayMode == DisplayMode.Compact)
            classes.Add("task-card-compact");
        else if (displayMode == DisplayMode.Grid)
            classes.Add("task-card-grid");

        return string.Join(" ", classes);
    }

    /// <summary>
    /// Возвращает набор CSS-классов заголовка на основе состояния рабочего элемента.
    /// </summary>
    public static string GetTitleClass(WorkItemBase item)
    {
        List<string> classes = [];

        if (item.Status == TaskStatus.Completed)
            classes.Add("title-completed");
        else if (item.Status == TaskStatus.Irrelevant)
            classes.Add("title-irrelevant");
        else if (item.Status == TaskStatus.NotConfigured)
            classes.Add("title-not-configured");

        return string.Join(" ", classes);
    }
}
