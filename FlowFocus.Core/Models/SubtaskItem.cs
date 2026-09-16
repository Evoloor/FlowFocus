using System.ComponentModel.DataAnnotations.Schema;

namespace FlowFocus.Core.Models;

/// <summary>
/// Модель подзадачи — отдельная сущность, привязанная к родительской задаче.
/// Не поддерживает повторение, связи, теги, условия, эскалации и собственные даты.
/// </summary>
public class SubtaskItem : WorkItemBase
{
    /// <summary>ID родительской задачи</summary>
    public int ParentTaskId { get; set; }

    /// <summary>Связанная родительская задача</summary>
    [ForeignKey(nameof(ParentTaskId))]
    public TaskItem? ParentTask { get; set; }

    /// <summary>Порядок сортировки внутри родительской задачи</summary>
    public int SortOrder { get; set; }
}
