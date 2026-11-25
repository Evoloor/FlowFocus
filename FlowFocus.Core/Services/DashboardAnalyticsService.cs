using FlowFocus.Core.Enums;
using FlowFocus.Core.Helpers;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Services;

/// <summary>
/// Реализация сервиса аналитики и метрик дашборда
/// </summary>
public class DashboardAnalyticsService : IDashboardAnalyticsService
{
    private static DateTime ResolveCurrentDate(DateTime? now) => now?.Date ?? TodoDay.Today.Date;

    private static int GetTaskWeight(TaskItem task, DashboardAggregationMode mode) =>
        mode == DashboardAggregationMode.TimeMinutes ? Math.Max(0, task.EstimatedMinutes ?? 0) : 1;

    /// <inheritdoc />
    public List<TaskItem> GetTasksForDateRange(IEnumerable<TaskItem> allTasks, DateRangeMode mode, DateTime? now = null)
    {
        var tasksList = allTasks.ToList();
        if (mode == DateRangeMode.AllTime || tasksList.Count == 0)
        {
            return tasksList;
        }

        var currentDate = ResolveCurrentDate(now);
        var daysWindow = mode == DateRangeMode.Recent ? 14 : 90;
        var minTargetCount = mode == DateRangeMode.Recent ? 100 : 300;
        var thresholdDate = currentDate.AddDays(-daysWindow);

        // Фильтрация задач за окно дней (по дате завершения, последнего изменения, планирования или создания)
        var windowTasks = tasksList.Where(t =>
            (t.CompletedDate.HasValue && t.CompletedDate.Value.Date >= thresholdDate) ||
            (t.LastChangesOn.Date >= thresholdDate) ||
            (t.ScheduledDate.HasValue && t.ScheduledDate.Value.Date >= thresholdDate) ||
            (t.CreatedDate.Date >= thresholdDate)
        ).ToList();

        // Формула Math.max(window_tasks.Count, limit)
        // Если задач за период меньше целевого минимума, дополняем последними выполненными задачами до целевого лимита
        if (windowTasks.Count < minTargetCount)
        {
            var completedTasks = tasksList
                .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue)
                .OrderByDescending(t => t.CompletedDate!.Value)
                .Take(minTargetCount)
                .ToList();

            var combined = windowTasks.UnionBy(completedTasks, t => t.Id).ToList();
            return combined;
        }

        return windowTasks;
    }

    /// <inheritdoc />
    public List<TaskItem> ApplyEntityScope(IEnumerable<TaskItem> tasks, DashboardFilter filter)
    {
        var tasksList = tasks.ToList();

        return filter.Scope switch
        {
            EntityScopeType.Active => tasksList
                .Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant)
                .ToList(),

            EntityScopeType.Completed => tasksList
                .Where(t => t.Status == TaskStatus.Completed)
                .ToList(),

            EntityScopeType.Tag => filter.TagFilterMode == TagFilterMode.Untagged
                ? tasksList.Where(t => t.Tags == null || t.Tags.Count == 0).ToList()
                : filter.TagId.HasValue
                    ? tasksList.Where(t => t.Tags != null && t.Tags.Any(tag => tag.TagId == filter.TagId.Value)).ToList()
                    : tasksList,

            EntityScopeType.Condition => filter.ConditionId.HasValue
                ? tasksList.Where(t => t.Conditions != null && t.Conditions.Any(c => c.ConditionId == filter.ConditionId.Value)).ToList()
                : tasksList,

            EntityScopeType.All or _ => tasksList
        };
    }

    /// <inheritdoc />
    public double CalculateActivityMetric(IEnumerable<TaskItem> tasks, DateRangeMode mode, DateTime? now = null)
    {
        var tasksList = tasks.ToList();
        var currentDate = ResolveCurrentDate(now);
        int totalDays;
        DateTime? thresholdDate = null;

        if (mode == DateRangeMode.Recent)
        {
            totalDays = 14;
            thresholdDate = currentDate.AddDays(-14);
        }
        else if (mode == DateRangeMode.Representative)
        {
            totalDays = 90;
            thresholdDate = currentDate.AddDays(-90);
        }
        else // AllTime
        {
            var earliestDate = tasksList
                .Select(t => t.CompletedDate ?? t.CreatedDate)
                .DefaultIfEmpty(currentDate)
                .Min().Date;

            totalDays = Math.Max(1, (currentDate - earliestDate).Days + 1);
        }

        var completedDatesQuery = tasksList
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue)
            .Select(t => t.CompletedDate!.Value.Date);

        if (thresholdDate.HasValue)
        {
            completedDatesQuery = completedDatesQuery
                .Where(d => d >= thresholdDate.Value && d <= currentDate);
        }
        else
        {
            completedDatesQuery = completedDatesQuery
                .Where(d => d <= currentDate);
        }

        var completedDates = completedDatesQuery.Distinct().ToHashSet();

        if (completedDates.Count == 0)
        {
            return 0.0;
        }

        var percentage = ((double)completedDates.Count / totalDays) * 100.0;
        return Math.Min(100.0, Math.Round(percentage, 1));
    }

    /// <inheritdoc />
    public int CalculateLongestDependencyChain(IEnumerable<TaskItem> allTasks)
    {
        var tasksList = allTasks.ToList();
        if (tasksList.Count == 0) return 0;

        var adj = BuildDependencyGraph(tasksList);
        HashSet<int> allNodes = new(tasksList.Select(t => t.Id));
        var maxLength = 0;

        foreach (var nodeId in allNodes)
        {
            HashSet<int> visitingPath = [];
            Dictionary<int, int> memo = new();
            var chainLength = GetMaxDepthDFS(nodeId, adj, visitingPath, memo);
            if (chainLength > maxLength)
            {
                maxLength = chainLength;
            }
        }

        return maxLength;
    }

    /// <inheritdoc />
    public BlockingImpactResult? CalculateMaxTransitiveBlocker(IEnumerable<TaskItem> tasks)
    {
        var activeTasks = tasks.Where(t => t.IsActive).ToList();
        if (activeTasks.Count == 0) return null;

        var activeMap = activeTasks
            .DistinctBy(t => t.Id)
            .ToDictionary(t => t.Id);
        var allowedNodes = activeMap.Keys.ToHashSet();
        var adj = BuildDependencyGraph(activeTasks, allowedNodes);

        TaskItem? bestTask = null;
        var bestCount = 0;

        foreach (var task in activeTasks)
        {
            var blockedCount = CountReachableNodes(task.Id, adj);
            if (blockedCount == 0) continue;

            if (bestTask == null || IsBetterBlockerCandidate(task, blockedCount, bestTask, bestCount))
            {
                bestTask = task;
                bestCount = blockedCount;
            }
        }

        return bestTask != null ? new BlockingImpactResult(bestTask, bestCount) : null;
    }

    private static bool IsBetterBlockerCandidate(TaskItem candidate, int candidateCount, TaskItem currentBest, int currentBestCount)
    {
        if (candidateCount != currentBestCount)
        {
            return candidateCount > currentBestCount;
        }

        var candidatePriority = candidate.Priority?.Order ?? int.MaxValue;
        var currentPriority = currentBest.Priority?.Order ?? int.MaxValue;
        if (candidatePriority != currentPriority)
        {
            return candidatePriority < currentPriority;
        }

        if (candidate.CreatedDate != currentBest.CreatedDate)
        {
            return candidate.CreatedDate < currentBest.CreatedDate;
        }

        return candidate.Id < currentBest.Id;
    }

    private static int CountReachableNodes(int startId, Dictionary<int, HashSet<int>> adj)
    {
        if (!adj.TryGetValue(startId, out var initialNeighbors) || initialNeighbors.Count == 0)
        {
            return 0;
        }

        HashSet<int> visited = [startId];
        Queue<int> queue = new();

        foreach (var neighbor in initialNeighbors)
        {
            if (visited.Add(neighbor))
            {
                queue.Enqueue(neighbor);
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adj.TryGetValue(current, out var nextNeighbors)) continue;

            foreach (var next in nextNeighbors)
            {
                if (visited.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return visited.Count - 1;
    }

    private static Dictionary<int, HashSet<int>> BuildDependencyGraph(IEnumerable<TaskItem> tasks, HashSet<int>? allowedNodes = null)
    {
        Dictionary<int, HashSet<int>> adj = new();

        foreach (var task in tasks)
        {
            if (task.Relations != null)
            {
                foreach (var rel in task.Relations)
                {
                    AddRelationEdge(adj, rel, task.Id, allowedNodes);
                }
            }

            if (task.InverseRelations != null)
            {
                foreach (var rel in task.InverseRelations)
                {
                    AddRelationEdge(adj, rel, task.Id, allowedNodes);
                }
            }
        }

        return adj;
    }

    private static void AddRelationEdge(
        Dictionary<int, HashSet<int>> adj,
        TaskRelation rel,
        int currentTaskId,
        HashSet<int>? allowedNodes = null)
    {
        var sourceId = rel.SourceTaskId != 0 ? rel.SourceTaskId : currentTaskId;
        var targetId = rel.TargetTaskId != 0 ? rel.TargetTaskId : currentTaskId;

        if (sourceId == targetId) return;

        int blockerId;
        int blockedId;

        if (rel.Type == RelationType.Blocks)
        {
            blockerId = sourceId;
            blockedId = targetId;
        }
        else if (rel.Type == RelationType.BlockedBy)
        {
            blockerId = targetId;
            blockedId = sourceId;
        }
        else
        {
            return;
        }

        if (allowedNodes != null && (!allowedNodes.Contains(blockerId) || !allowedNodes.Contains(blockedId)))
        {
            return;
        }

        if (!adj.TryGetValue(blockerId, out var set))
        {
            set = [];
            adj[blockerId] = set;
        }
        set.Add(blockedId);
    }

    private static int GetMaxDepthDFS(int nodeId, Dictionary<int, HashSet<int>> adj, HashSet<int> visitingPath, Dictionary<int, int> memo)
    {
        if (visitingPath.Contains(nodeId))
        {
            return 0;
        }

        if (memo.TryGetValue(nodeId, out var cachedDepth))
        {
            return cachedDepth;
        }

        visitingPath.Add(nodeId);

        var maxChildDepth = 0;
        if (adj.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (visitingPath.Contains(neighbor))
                {
                    continue;
                }

                var depth = 1 + GetMaxDepthDFS(neighbor, adj, visitingPath, memo);
                if (depth > maxChildDepth)
                {
                    maxChildDepth = depth;
                }
            }
        }

        visitingPath.Remove(nodeId);

        memo[nodeId] = maxChildDepth;
        return maxChildDepth;
    }

    /// <inheritdoc />
    public Dictionary<DayOfWeek, double> CalculateWeekdayDistribution(
        IEnumerable<TaskItem> tasks,
        DashboardAggregationMode mode = DashboardAggregationMode.TaskCount)
    {
        Dictionary<DayOfWeek, double> result = new()
        {
            { DayOfWeek.Monday, 0.0 },
            { DayOfWeek.Tuesday, 0.0 },
            { DayOfWeek.Wednesday, 0.0 },
            { DayOfWeek.Thursday, 0.0 },
            { DayOfWeek.Friday, 0.0 },
            { DayOfWeek.Saturday, 0.0 },
            { DayOfWeek.Sunday, 0.0 }
        };

        var completedTasks = tasks
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue)
            .Where(t => mode != DashboardAggregationMode.TimeMinutes || t.EstimatedMinutes is > 0)
            .ToList();

        if (completedTasks.Count == 0)
        {
            return result;
        }

        var weightByDate = completedTasks
            .GroupBy(t => t.CompletedDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Sum(t => GetTaskWeight(t, mode)));

        var datesByWeekday = weightByDate.Keys.GroupBy(d => d.DayOfWeek);

        foreach (var group in datesByWeekday)
        {
            var weekday = group.Key;
            var activeDates = group.ToList();
            var totalWeightForWeekday = activeDates.Sum(d => weightByDate[d]);
            var activeDaysCount = activeDates.Count;

            if (activeDaysCount > 0)
            {
                result[weekday] = Math.Round((double)totalWeightForWeekday / activeDaysCount, 1);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public WeeklyComparisonDto CalculateWeeklyComparison(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null)
    {
        var today = ResolveCurrentDate(now);
        var currentStart = today.AddDays(-6);
        var previousStart = today.AddDays(-13);
        var previousEnd = today.AddDays(-7);

        var scopedTasks = ApplyEntityScope(allTasks, filter);
        var completed = scopedTasks
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue)
            .ToList();

        var currentWeek = completed
            .Where(t =>
            {
                var d = t.CompletedDate!.Value.Date;
                return d >= currentStart && d <= today;
            })
            .ToList();

        var previousWeek = completed
            .Where(t =>
            {
                var d = t.CompletedDate!.Value.Date;
                return d >= previousStart && d <= previousEnd;
            })
            .ToList();

        return new WeeklyComparisonDto
        {
            CompletedTasksCount = new MetricPeriodComparison
            {
                CurrentValue = currentWeek.Count,
                PreviousValue = previousWeek.Count
            },
            CompletedTimeMinutes = new MetricPeriodComparison
            {
                CurrentValue = currentWeek.Sum(t => Math.Max(0, t.EstimatedMinutes ?? 0)),
                PreviousValue = previousWeek.Sum(t => Math.Max(0, t.EstimatedMinutes ?? 0))
            },
            AverageComplexity = new MetricPeriodComparison
            {
                CurrentValue = CalculateNullableAverage(currentWeek, t => t.Complexity),
                PreviousValue = CalculateNullableAverage(previousWeek, t => t.Complexity)
            },
            AverageInterest = new MetricPeriodComparison
            {
                CurrentValue = CalculateNullableAverage(currentWeek, t => t.Interest),
                PreviousValue = CalculateNullableAverage(previousWeek, t => t.Interest)
            }
        };
    }

    private static double? CalculateNullableAverage(List<TaskItem> tasks, Func<TaskItem, int?> selector)
    {
        var values = tasks
            .Select(selector)
            .Where(v => v.HasValue)
            .Select(v => (double)v!.Value)
            .ToList();

        return values.Count > 0 ? Math.Round(values.Average(), 1) : null;
    }

    /// <inheritdoc />
    public DashboardDataSlices PrepareDataSlices(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null)
    {
        var allList = allTasks.ToList();

        // 1. Фильтрация по временному диапазону
        var dateFiltered = GetTasksForDateRange(allList, filter.DateRange, now);

        // 2. Фильтрация по области сущностей (Scope)
        var fullyFiltered = ApplyEntityScope(dateFiltered, filter);

        // 3. Фильтрация созданных задач с учетом Scope
        List<TaskItem>? createdInScope = null;
        if (filter.DateRange != DateRangeMode.AllTime)
        {
            var currentDate = ResolveCurrentDate(now);
            var daysWindow = filter.DateRange == DateRangeMode.Recent ? 14 : 90;
            var thresholdDate = currentDate.AddDays(-daysWindow);
            createdInScope = fullyFiltered.Where(t => t.CreatedDate.Date >= thresholdDate).ToList();
        }

        return new DashboardDataSlices(allList, dateFiltered, fullyFiltered, createdInScope);
    }

    /// <inheritdoc />
    public DashboardMetricsDto CalculateMetrics(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null)
    {
        var slices = PrepareDataSlices(allTasks, filter, now);
        var mode = filter.AggregationMode;

        DashboardMetricsDto dto = new()
        {
            AggregationMode = mode,
            CreatedTasksCount = slices.CreatedInScope?.Count,
            CreatedTasksTimeMinutes = slices.CreatedInScope?.Sum(t => Math.Max(0, t.EstimatedMinutes ?? 0)),
            WeeklyComparison = CalculateWeeklyComparison(slices.All, filter, now)
        };

        if (slices.FullyFiltered.Count == 0)
        {
            foreach (DayOfWeek dow in Enum.GetValues(typeof(DayOfWeek)))
            {
                dto.WeekdayAverages[dow] = 0.0;
            }
            return dto;
        }

        // === Summary Grid Metrics ===
        dto.ActivityPercentage = CalculateActivityMetric(slices.FullyFiltered, filter.DateRange, now);

        dto.TotalTasksCount = slices.FullyFiltered.Count;
        dto.TotalSubtasksCount = slices.FullyFiltered.Sum(t => t.Subtasks?.Count ?? 0);
        dto.TasksWithTimeEstimateCount = slices.FullyFiltered.Count(t => t.EstimatedMinutes is > 0);

        var completionPool = slices.FullyFiltered
            .Where(t => !t.IsRecurring && (t.Status == TaskStatus.Completed || t.Status.IsActive()))
            .ToList();
        var completedWeight = completionPool
            .Where(t => t.Status == TaskStatus.Completed)
            .Sum(t => GetTaskWeight(t, mode));
        var totalCompletionWeight = completionPool
            .Sum(t => GetTaskWeight(t, mode));
        dto.CompletionRatePercentage = totalCompletionWeight > 0
            ? Math.Round(((double)completedWeight / totalCompletionWeight) * 100.0, 1)
            : 0.0;

        // === Task Structure & Types ===
        dto.StatusDistribution = DistributionHelper.CalculateEnumDistribution<TaskItem, TaskStatus, StatusDistribution>(
            slices.FullyFiltered,
            t => t.Status,
            t => GetTaskWeight(t, mode));
        dto.DateSourceDistribution = DistributionHelper.CalculateEnumDistribution<TaskItem, DateSource, DateSourceDistribution>(
            slices.FullyFiltered,
            t => t.DateSource,
            t => GetTaskWeight(t, mode));
        dto.TagsDistribution = DistributionHelper.CalculateCollectionDistribution(
            slices.FullyFiltered,
            t => t.Tags?.Where(tt => tt.Tag != null).Select(tt => tt.Tag!.Name),
            t => GetTaskWeight(t, mode));
        dto.ConditionsDistribution = DistributionHelper.CalculateCollectionDistribution(
            slices.FullyFiltered,
            t => t.Conditions?.Where(tc => tc.Condition != null).Select(tc => tc.Condition!.Name),
            t => GetTaskWeight(t, mode));

        var untaggedTasks = slices.FullyFiltered.Where(t => t.Tags == null || t.Tags.Count == 0).ToList();
        dto.UntaggedTasksCount = untaggedTasks.Count;
        dto.UntaggedTasksValue = untaggedTasks.Sum(t => GetTaskWeight(t, mode));
        var totalFilteredWeight = slices.FullyFiltered.Sum(t => GetTaskWeight(t, mode));
        dto.UntaggedTasksPercentage = totalFilteredWeight > 0
            ? Math.Round(((double)dto.UntaggedTasksValue / totalFilteredWeight) * 100.0, 1)
            : 0.0;

        // === Deep Analytics Grid ===
        dto.FilteredCount = slices.FullyFiltered.Count;
        dto.FilteredTotalTimeMinutes = slices.FullyFiltered.Sum(t => t.EstimatedMinutes ?? 0);

        var tasksWithTime = slices.FullyFiltered.Where(t => t.EstimatedMinutes.HasValue).Select(t => t.EstimatedMinutes!.Value).ToList();
        if (tasksWithTime.Count > 0)
        {
            dto.FilteredAvgTimeMinutes = Math.Round(tasksWithTime.Average(), 1);
            dto.FilteredMinTimeMinutes = tasksWithTime.Min();
            dto.FilteredMaxTimeMinutes = tasksWithTime.Max();
        }

        var tasksWithComplexity = slices.FullyFiltered.Where(t => t.Complexity.HasValue).Select(t => t.Complexity!.Value).ToList();
        if (tasksWithComplexity.Count > 0)
        {
            dto.FilteredAvgComplexity = Math.Round(tasksWithComplexity.Average(), 1);
            dto.FilteredMinComplexity = tasksWithComplexity.Min();
            dto.FilteredMaxComplexity = tasksWithComplexity.Max();
        }

        var tasksWithInterest = slices.FullyFiltered.Where(t => t.Interest.HasValue).Select(t => t.Interest!.Value).ToList();
        if (tasksWithInterest.Count > 0)
        {
            dto.FilteredAvgInterest = Math.Round(tasksWithInterest.Average(), 1);
            dto.FilteredMinInterest = tasksWithInterest.Min();
            dto.FilteredMaxInterest = tasksWithInterest.Max();
        }

        var tasksWithPriority = slices.FullyFiltered.Where(t => t.Priority != null).ToList();
        if (tasksWithPriority.Count > 0)
        {
            var maxPriorityTask = tasksWithPriority.MinBy(t => t.Priority!.Order);
            var minPriorityTask = tasksWithPriority.MaxBy(t => t.Priority!.Order);

            dto.FilteredMaxPriority = maxPriorityTask?.Priority!.Name;
            dto.FilteredMinPriority = minPriorityTask?.Priority!.Name;

            var avgOrder = tasksWithPriority.Average(t => t.Priority!.Order);
            var distinctPriorities = tasksWithPriority.Select(t => t.Priority!).DistinctBy(p => p.Id).ToList();
            var avgPriority = distinctPriorities.MinBy(p => Math.Abs(p.Order - avgOrder));
            dto.FilteredAvgPriority = avgPriority?.Name;
        }

        // === Weekday Distribution ===
        dto.WeekdayAverages = CalculateWeekdayDistribution(slices.FullyFiltered, mode);

        // === Records & Interest-Priority Analytics ===
        dto.Records = CalculateRecords(slices.FullyFiltered);
        dto.InterestPriorityDistribution = CalculateInterestPriorityDistribution(slices.FullyFiltered);

        // === Metric Histograms ===
        dto.InterestHistogram = CalculateInterestHistogram(slices.FullyFiltered, mode);
        dto.ComplexityHistogram = CalculateComplexityHistogram(slices.FullyFiltered, mode);
        dto.PriorityHistogram = CalculatePriorityHistogram(slices.FullyFiltered, mode);
        dto.TimeHistogram = CalculateTimeHistogram(slices.FullyFiltered, mode);
        dto.PriorityInterestHistogram = CalculatePriorityInterestHistogram(slices.FullyFiltered);

        return dto;
    }

    private List<DashboardRecordItem> CalculateRecords(List<TaskItem> tasks)
    {
        List<DashboardRecordItem> rawRecords = [];
        var completed = tasks.Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue).ToList();
        var byDate = completed.GroupBy(t => t.CompletedDate!.Value.Date).ToList();

        // 1. Больше всего завершённых задач за день
        if (byDate.Count > 0)
        {
            var maxCountGroup = byDate.OrderByDescending(g => g.Count()).First();
            rawRecords.Add(new()
            {
                Title = "Макс. завершённых задач за день",
                Value = $"{maxCountGroup.Count()} задач",
                Date = maxCountGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Макс. завершённых задач за день", Value = "-", Date = null });
        }

        // 2. Больше всего сумма времени в завершённых за день дел
        if (byDate.Count > 0 && byDate.Any(g => g.Sum(t => t.EstimatedMinutes ?? 0) > 0))
        {
            var maxTimeGroup = byDate.OrderByDescending(g => g.Sum(t => t.EstimatedMinutes ?? 0)).First();
            var totalMins = maxTimeGroup.Sum(t => t.EstimatedMinutes ?? 0);
            rawRecords.Add(new()
            {
                Title = "Макс. сумма времени за день",
                Value = DurationFormatHelper.FormatMinutes(totalMins),
                Date = maxTimeGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Макс. сумма времени за день", Value = "-", Date = null });
        }

        // Дни с >= 3 завершенными задачами и имеющие оценки интересности
        var eligibleInterestGroups = byDate
            .Where(g => g.Count() >= 3 && g.Any(t => t.Interest.HasValue))
            .ToList();

        // 3. Самая низкая интересность выполненных за день (>= 3 задач)
        if (eligibleInterestGroups.Count > 0)
        {
            var minInterestGroup = eligibleInterestGroups
                .OrderBy(g => g.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value))
                .First();
            var avgInt = minInterestGroup.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value);

            rawRecords.Add(new()
            {
                Title = "Мин. интересность за день (≥3 задач)",
                Value = avgInt.ToString("F1"),
                Date = minInterestGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Мин. интересность за день (≥3 задач)", Value = "-", Date = null });
        }

        // 4. Максимальная интересность выполненных за день (>= 3 задач)
        if (eligibleInterestGroups.Count > 0)
        {
            var maxInterestGroup = eligibleInterestGroups
                .OrderByDescending(g => g.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value))
                .First();
            var avgInt = maxInterestGroup.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value);

            rawRecords.Add(new()
            {
                Title = "Макс. интересность за день (≥3 задач)",
                Value = avgInt.ToString("F1"),
                Date = maxInterestGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Макс. интересность за день (≥3 задач)", Value = "-", Date = null });
        }

        // 5. Макс сложность выполненной задачи
        var maxCompTask = completed.Where(t => t.Complexity.HasValue).OrderByDescending(t => t.Complexity!.Value).FirstOrDefault();
        if (maxCompTask != null)
        {
            rawRecords.Add(new()
            {
                Title = "Макс. сложность выполненной задачи",
                Value = $"{maxCompTask.Complexity} ({maxCompTask.Title})",
                Date = maxCompTask.CompletedDate?.Date
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Макс. сложность выполненной задачи", Value = "-", Date = null });
        }

        // 6. Макс продолжительность выполненной задачи
        var maxDurTask = completed.Where(t => t.EstimatedMinutes.HasValue).OrderByDescending(t => t.EstimatedMinutes!.Value).FirstOrDefault();
        if (maxDurTask != null)
        {
            rawRecords.Add(new()
            {
                Title = "Макс. продолжительность задачи",
                Value = $"{DurationFormatHelper.FormatMinutes(maxDurTask.EstimatedMinutes!.Value)} ({maxDurTask.Title})",
                Date = maxDurTask.CompletedDate?.Date
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Макс. продолжительность задачи", Value = "-", Date = null });
        }

        // 7. Самая длинная цепочка блокировок
        var chainLength = CalculateLongestDependencyChain(tasks);
        if (chainLength > 1)
        {
            rawRecords.Add(new()
            {
                Title = "Самая длинная цепочка блокировок",
                Value = $"{chainLength} связей",
                Date = null
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Самая длинная цепочка блокировок", Value = "-", Date = null });
        }

        // 8. Задача, блокирующая больше всего активных задач по цепочке
        var maxBlocker = CalculateMaxTransitiveBlocker(tasks);
        if (maxBlocker != null)
        {
            rawRecords.Add(new()
            {
                Title = "Блокирует больше всего задач",
                Value = $"{maxBlocker.BlockedTasksCount} задач ({maxBlocker.BlockerTask.Title})",
                Date = null
            });
        }
        else
        {
            rawRecords.Add(new() { Title = "Блокирует больше всего задач", Value = "-", Date = null });
        }

        // Сортировка по убыванию даты (если дата null, идут в конец)
        var sortedRecords = rawRecords
            .OrderByDescending(r => r.Date ?? DateTime.MinValue)
            .ToList();

        return sortedRecords;
    }

    private static Dictionary<int, double> CalculateInterestPriorityDistribution(List<TaskItem> tasks)
    {
        Dictionary<int, double> result = new();

        for (var interest = 1; interest <= 10; interest++)
        {
            var matchingTasks = tasks
                .Where(t => t.Interest == interest && (t.Priority != null || t.PriorityId.HasValue))
                .ToList();

            if (matchingTasks.Count > 0)
            {
                var avgPriority = matchingTasks.Average(t => t.Priority?.Order ?? t.PriorityId ?? 0);
                result[interest] = Math.Round(avgPriority, 1);
            }
            else
            {
                result[interest] = 0.0;
            }
        }

        return result;
    }

    private static Dictionary<string, int> CalculateInterestHistogram(List<TaskItem> tasks, DashboardAggregationMode mode)
    {
        var interestTasks = tasks.Where(t => t.Interest.HasValue).ToList();
        if (interestTasks.Count == 0) return new();

        return interestTasks
            .GroupBy(t => t.Interest!.Value)
            .OrderBy(g => g.Key)
            .Select(g => (Key: g.Key.ToString(), Sum: g.Sum(t => GetTaskWeight(t, mode))))
            .Where(x => x.Sum > 0)
            .ToDictionary(x => x.Key, x => x.Sum);
    }

    private static Dictionary<string, int> CalculateComplexityHistogram(List<TaskItem> tasks, DashboardAggregationMode mode)
    {
        var compTasks = tasks
            .Where(t => t.Complexity.HasValue && GetTaskWeight(t, mode) > 0)
            .ToList();
        if (compTasks.Count == 0) return new();

        var values = compTasks.Select(t => t.Complexity!.Value).OrderBy(v => v).ToList();
        var distinctValues = values.Distinct().ToList();

        // Если уникальных значений мало (<= 8), группируем по точным значениям
        if (distinctValues.Count <= 8)
        {
            return compTasks
                .GroupBy(t => t.Complexity!.Value)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key.ToString(), g => g.Sum(t => GetTaskWeight(t, mode)));
        }

        // Иначе динамически группируем по диапазонным бакетам на основе min и max имеющихся БД-значений
        var minVal = values.Min();
        var maxVal = values.Max();
        var binCount = 5;
        var step = Math.Max(1, (int)Math.Ceiling((double)(maxVal - minVal + 1) / binCount));

        Dictionary<string, int> result = new();
        for (var i = 0; i < binCount; i++)
        {
            var start = minVal + i * step;
            var end = i == binCount - 1 ? maxVal : Math.Min(maxVal, start + step - 1);
            if (start > maxVal) break;

            var label = start == end ? $"{start}" : $"{start}–{end}";
            var sum = compTasks
                .Where(t => t.Complexity!.Value >= start && t.Complexity!.Value <= end)
                .Sum(t => GetTaskWeight(t, mode));
            if (sum > 0 || result.Count > 0)
            {
                result[label] = sum;
            }

            if (end >= maxVal) break;
        }

        return result;
    }

    private static Dictionary<string, int> CalculatePriorityHistogram(List<TaskItem> tasks, DashboardAggregationMode mode)
    {
        var priorityTasks = tasks
            .Where(t => t.Priority != null || t.PriorityId.HasValue)
            .ToList();

        if (priorityTasks.Count == 0) return new();

        return priorityTasks
            .GroupBy(t => t.Priority?.Name ?? "Не указан")
            .OrderBy(g => g.First().Priority?.Order ?? int.MaxValue)
            .Select(g => (Key: g.Key, Sum: g.Sum(t => GetTaskWeight(t, mode))))
            .Where(x => x.Sum > 0)
            .ToDictionary(x => x.Key, x => x.Sum);
    }

    private static Dictionary<string, int> CalculateTimeHistogram(List<TaskItem> tasks, DashboardAggregationMode mode)
    {
        var timeTasks = tasks.Where(t => t.EstimatedMinutes.HasValue).ToList();
        if (timeTasks.Count == 0) return new();

        // Группировка времени по человекопонятным интервалам
        var ranges = new (string Label, Func<int, bool> Match)[]
        {
            ("≤15 мин", m => m <= 15),
            ("16–30 мин", m => m is > 15 and <= 30),
            ("31–60 мин", m => m is > 30 and <= 60),
            ("1–2 ч", m => m is > 60 and <= 120),
            ("2–4 ч", m => m is > 120 and <= 240),
            ("4–8 ч", m => m is > 240 and <= 480),
            (">8 ч", m => m > 480)
        };

        Dictionary<string, int> result = new();
        foreach (var range in ranges)
        {
            var sum = timeTasks
                .Where(t => range.Match(t.EstimatedMinutes!.Value))
                .Sum(t => GetTaskWeight(t, mode));
            if (sum > 0)
            {
                result[range.Label] = sum;
            }
        }

        return result;
    }

    public static List<PriorityInterestItemDto> CalculatePriorityInterestHistogram(List<TaskItem> tasks)
    {
        var priorityTasks = tasks
            .Where(t => (t.Priority != null || t.PriorityId.HasValue) && t.Interest.HasValue)
            .ToList();

        if (priorityTasks.Count == 0) return [];

        var grouped = priorityTasks
            .GroupBy(t => t.Priority?.Name ?? "Не указан")
            .Select(g =>
            {
                var firstPriority = g.First().Priority;
                var order = firstPriority?.Order ?? int.MaxValue;
                var color = !string.IsNullOrWhiteSpace(firstPriority?.Color) ? firstPriority.Color : "#808080";
                var avgInterest = Math.Round(g.Average(t => (double)t.Interest!.Value), 1);
                return new PriorityInterestItemDto
                {
                    PriorityName = g.Key,
                    PriorityOrder = order,
                    Color = color,
                    AverageInterest = avgInterest,
                    TaskCount = g.Count()
                };
            })
            .OrderBy(item => item.PriorityOrder)
            .ToList();

        return grouped;
    }
}
