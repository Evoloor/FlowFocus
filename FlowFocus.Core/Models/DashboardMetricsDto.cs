using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Models;

/// <summary>
/// Агрегированные метрики для дашборда
/// </summary>
public class DashboardMetricsDto
{
    // === Summary Grid ===
    /// <summary>Активность в приложении (% дней с хотя бы 1 выполненной задачей)</summary>
    public double ActivityPercentage { get; set; }

    /// <summary>Всего задач (число верхнеуровневых задач)</summary>
    public int TotalTasksCount { get; set; }

    /// <summary>Всего подзадач (число вложенных подзадач)</summary>
    public int TotalSubtasksCount { get; set; }

    /// <summary>Процент оставшихся (активных) задач</summary>
    public double CompletionRatePercentage { get; set; }

    /// <summary>Создано задач за выбранный период (null если за все время)</summary>
    public int? CreatedTasksCount { get; set; }

    // === Task Structure & Types ===
    /// <summary>Распределение по статусам</summary>
    public Dictionary<TaskStatus, int> StatusDistribution { get; set; } = new();

    /// <summary>Распределение по источникам даты</summary>
    public Dictionary<DateSource, int> DateSourceDistribution { get; set; } = new();

    /// <summary>Распределение по тегам</summary>
    public Dictionary<string, int> TagsDistribution { get; set; } = new();

    /// <summary>Распределение по условиям</summary>
    public Dictionary<string, int> ConditionsDistribution { get; set; } = new();

    // === Deep Analytics Grid ===
    /// <summary>Количество задач с учётом Scope</summary>
    public int FilteredCount { get; set; }

    /// <summary>Суммарное время (минуты) с учётом Scope</summary>
    public int FilteredTotalTimeMinutes { get; set; }

    /// <summary>Среднее время (минуты) с учётом Scope (только non-null)</summary>
    public double? FilteredAvgTimeMinutes { get; set; }

    /// <summary>Минимальное время (минуты) с учётом Scope (только non-null)</summary>
    public int? FilteredMinTimeMinutes { get; set; }

    /// <summary>Максимальное время (минуты) с учётом Scope (только non-null)</summary>
    public int? FilteredMaxTimeMinutes { get; set; }

    /// <summary>Средняя сложность с учётом Scope (только non-null)</summary>
    public double? FilteredAvgComplexity { get; set; }

    /// <summary>Минимальная сложность с учётом Scope (только non-null)</summary>
    public int? FilteredMinComplexity { get; set; }

    /// <summary>Максимальная сложность с учётом Scope (только non-null)</summary>
    public int? FilteredMaxComplexity { get; set; }

    /// <summary>Средняя интересность с учётом Scope (только non-null)</summary>
    public double? FilteredAvgInterest { get; set; }

    /// <summary>Минимальная интересность с учётом Scope (только non-null)</summary>
    public int? FilteredMinInterest { get; set; }

    /// <summary>Максимальная интересность с учётом Scope (только non-null)</summary>
    public int? FilteredMaxInterest { get; set; }

    /// <summary>Средний приоритет с учётом Scope (только non-null)</summary>
    public string? FilteredAvgPriority { get; set; }

    /// <summary>Минимальный приоритет с учётом Scope (только non-null)</summary>
    public string? FilteredMinPriority { get; set; }

    /// <summary>Максимальный приоритет с учётом Scope (только non-null)</summary>
    public string? FilteredMaxPriority { get; set; }

    // === Weekday Analytics ===
    /// <summary>Среднее распределение выполненных дел по дням недели (Пн-Вс)</summary>
    public Dictionary<DayOfWeek, double> WeekdayAverages { get; set; } = new();

    // === Records & Analytics ===
    /// <summary>Список рекордов</summary>
    public List<DashboardRecordItem> Records { get; set; } = [];

    /// <summary>Распределение среднего приоритета в зависимости от интересности (1..10)</summary>
    public Dictionary<int, double> InterestPriorityDistribution { get; set; } = new();

    // === Metric Histogram Data ===
    /// <summary>Гистограмма распределения по интересности (1-10)</summary>
    public Dictionary<string, int> InterestHistogram { get; set; } = new();

    /// <summary>Гистограмма распределения по сложности (сгруппированная по БД-значениям)</summary>
    public Dictionary<string, int> ComplexityHistogram { get; set; } = new();

    /// <summary>Гистограмма распределения по приоритетам</summary>
    public Dictionary<string, int> PriorityHistogram { get; set; } = new();

    /// <summary>Гистограмма распределения по времени (сгруппированная по БД-значениям)</summary>
    public Dictionary<string, int> TimeHistogram { get; set; } = new();

    /// <summary>Флаг пустого состояния (когда нет задач)</summary>
    public bool IsEmpty => TotalTasksCount == 0 && FilteredCount == 0;
}
