namespace FlowFocus.WebApp.Components;

/// <summary>
/// Фильтр для списка задач
/// </summary>
public class TaskListFilter
{
    public TaskListFilterType Type { get; set; }
}

/// <summary>
/// Тип фильтра для списка задач
/// </summary>
public enum TaskListFilterType
{
    None,
    Today,
    Tomorrow,
    NotConfigured,
    Overdue,
    All
}




