using FlowFocus.Core.Enums;

namespace FlowFocus.Core.Models;

/// <summary>
/// Состояние фильтрации дашборда
/// </summary>
public class DashboardFilter
{
    /// <summary>Глобальный фильтр по временным отрезкам</summary>
    public DateRangeMode DateRange { get; set; } = DateRangeMode.AllTime;

    /// <summary>Область фильтрации сущностей</summary>
    public EntityScopeType Scope { get; set; } = EntityScopeType.All;

    /// <summary>ID выбранного тега (для Scope == Tag)</summary>
    public int? TagId { get; set; }

    /// <summary>Режим фильтрации по тегу (конкретный тег или задачи без тегов)</summary>
    public TagFilterMode TagFilterMode { get; set; } = TagFilterMode.SpecificTag;

    /// <summary>ID выбранного условия (для Scope == Condition)</summary>
    public int? ConditionId { get; set; }

    /// <summary>Режим агрегации метрик (количество задач или время)</summary>
    public DashboardAggregationMode AggregationMode { get; set; } = DashboardAggregationMode.TaskCount;
}

/// <summary>
/// Значение выбора в выпадающем списке фильтрации по тегам
/// </summary>
public readonly record struct TagFilterSelection(TagFilterMode Mode, int? TagId);
