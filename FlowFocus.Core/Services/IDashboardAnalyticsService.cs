using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;

namespace FlowFocus.Core.Services;

/// <summary>
/// Сервис расчета аналитики и метрик для дашборда
/// </summary>
public interface IDashboardAnalyticsService
{
    /// <summary>
    /// Фильтрация задач по временному отрезку (DateRangeMode)
    /// </summary>
    List<TaskItem> GetTasksForDateRange(IEnumerable<TaskItem> allTasks, DateRangeMode mode, DateTime? now = null);

    /// <summary>
    /// Применение области фильтрации сущностей (EntityScopeType)
    /// </summary>
    List<TaskItem> ApplyEntityScope(IEnumerable<TaskItem> tasks, DashboardFilter filter);

    /// <summary>
    /// Расчет активности (% дней за выбранный период с хотя бы 1 выполненной задачей)
    /// </summary>
    double CalculateActivityMetric(IEnumerable<TaskItem> tasks, DateRangeMode mode, DateTime? now = null);

    /// <summary>
    /// Расчет наидлиннейшей цепочки зависимостей (включая завершенные)
    /// </summary>
    int CalculateLongestDependencyChain(IEnumerable<TaskItem> allTasks);

    /// <summary>
    /// Расчет среднего количества выполненных задач по дням недели (без учета дней без задач)
    /// </summary>
    Dictionary<DayOfWeek, double> CalculateWeekdayDistribution(IEnumerable<TaskItem> tasks);

    /// <summary>
    /// Расчет агрегированных метрик дашборда
    /// </summary>
    DashboardMetricsDto CalculateMetrics(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null);
}
