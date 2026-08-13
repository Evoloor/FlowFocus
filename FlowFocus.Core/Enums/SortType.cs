namespace FlowFocus.Core.Enums;

/// <summary>
/// Тип сортировки списка задач
/// </summary>
public enum SortType
{
    /// <summary>По релевантности (приоритет, интерес, время)</summary>
    Relevance,
    /// <summary>По дате назначения (возр.)</summary>
    DateAsc,
    /// <summary>По дате назначения (убыв.)</summary>
    DateDesc,
    /// <summary>По дате создания (возр.)</summary>
    DateCreatedAsc,
    /// <summary>По дате создания (убыв.)</summary>
    DateCreatedDesc,
    /// <summary>По сложности (возр.)</summary>
    ComplexityAsc,
    /// <summary>По сложности (убыв.)</summary>
    ComplexityDesc,
    /// <summary>По интересу (возр.)</summary>
    InterestAsc,
    /// <summary>По интересу (убыв.)</summary>
    InterestDesc,
    /// <summary>По времени выполнения (возр.)</summary>
    DurationAsc,
    /// <summary>По времени выполнения (убыв.)</summary>
    DurationDesc
}