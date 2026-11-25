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
    /// Формирование срезов данных для аналитического конвейера дашборда
    /// </summary>
    DashboardDataSlices PrepareDataSlices(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null);

    /// <summary>
    /// Расчет активности (% дней за выбранный период с хотя бы 1 выполненной задачей)
    /// </summary>
    double CalculateActivityMetric(IEnumerable<TaskItem> tasks, DateRangeMode mode, DateTime? now = null);

    /// <summary>
    /// Расчет наидлиннейшей цепочки зависимостей (включая завершенные)
    /// </summary>
    int CalculateLongestDependencyChain(IEnumerable<TaskItem> allTasks);

    /// <summary>
    /// Поиск активной задачи, которая транзитивно блокирует наибольшее количество других активных задач
    /// </summary>
    BlockingImpactResult? CalculateMaxTransitiveBlocker(IEnumerable<TaskItem> tasks);

    /// <summary>
    /// Расчет среднего количества выполненных задач или времени по дням недели (без учета дней без задач)
    /// </summary>
    Dictionary<DayOfWeek, double> CalculateWeekdayDistribution(
        IEnumerable<TaskItem> tasks,
        DashboardAggregationMode mode = DashboardAggregationMode.TaskCount);

    /// <summary>
    /// Сравнение выполненных задач за последние 7 туду-дней против предыдущих 7 туду-дней
    /// </summary>
    WeeklyComparisonDto CalculateWeeklyComparison(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null);

    /// <summary>
    /// Расчет агрегированных метрик дашборда
    /// </summary>
    DashboardMetricsDto CalculateMetrics(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null);
}
