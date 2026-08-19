namespace FlowFocus.Core.Models;

/// <summary>
/// Подготовленные срезы данных для аналитического конвейера дашборда
/// </summary>
public class DashboardDataSlices
{
    /// <summary>Все задачи без фильтрации</summary>
    public List<TaskItem> All { get; }

    /// <summary>Задачи, отфильтрованные по временному окну</summary>
    public List<TaskItem> DateFiltered { get; }

    /// <summary>Основная рабочая выборка: задачи с учетом временного окна и области сущностей (Scope)</summary>
    public List<TaskItem> FullyFiltered { get; }

    /// <summary>Задачи, созданные в выбранном временном окне с учетом области сущностей (null если фильтр «За все время»)</summary>
    public List<TaskItem>? CreatedInScope { get; }

    public DashboardDataSlices(
        List<TaskItem> all,
        List<TaskItem> dateFiltered,
        List<TaskItem> fullyFiltered,
        List<TaskItem>? createdInScope)
    {
        All = all;
        DateFiltered = dateFiltered;
        FullyFiltered = fullyFiltered;
        CreatedInScope = createdInScope;
    }
}
