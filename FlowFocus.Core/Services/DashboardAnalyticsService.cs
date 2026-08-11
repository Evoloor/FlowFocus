using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Core.Services;

/// <summary>
/// Реализация сервиса аналитики и метрик дашборда
/// </summary>
public class DashboardAnalyticsService : IDashboardAnalyticsService
{
    /// <inheritdoc />
    public List<TaskItem> GetTasksForDateRange(IEnumerable<TaskItem> allTasks, DateRangeMode mode, DateTime? now = null)
    {
        var tasksList = allTasks.ToList();
        if (mode == DateRangeMode.AllTime || tasksList.Count == 0)
        {
            return tasksList;
        }

        var currentDate = (now ?? DateTime.UtcNow).Date;
        int daysWindow = mode == DateRangeMode.Recent ? 14 : 90;
        int minTargetCount = mode == DateRangeMode.Recent ? 100 : 300;
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

            EntityScopeType.Tag => filter.TagId.HasValue
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
        var completedDates = tasksList
            .Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue)
            .Select(t => t.CompletedDate!.Value.Date)
            .Distinct()
            .ToHashSet();

        if (completedDates.Count == 0)
        {
            return 0.0;
        }

        var currentDate = (now ?? DateTime.UtcNow).Date;
        int totalDays;

        if (mode == DateRangeMode.Recent)
        {
            totalDays = 14;
        }
        else if (mode == DateRangeMode.Representative)
        {
            totalDays = 90;
        }
        else // AllTime
        {
            var earliestDate = tasksList
                .Select(t => t.CompletedDate ?? t.CreatedDate)
                .Min().Date;

            totalDays = Math.Max(1, (currentDate - earliestDate).Days + 1);
        }

        double percentage = ((double)completedDates.Count / totalDays) * 100.0;
        return Math.Round(percentage, 1);
    }

    /// <inheritdoc />
    public int CalculateLongestDependencyChain(IEnumerable<TaskItem> allTasks)
    {
        var tasksList = allTasks.ToList();
        if (tasksList.Count == 0) return 0;

        // Построение графа зависимостей: A blocks B => A -> B
        var adj = new Dictionary<int, HashSet<int>>();
        var allNodes = new HashSet<int>(tasksList.Select(t => t.Id));

        foreach (var task in tasksList)
        {
            if (task.Relations != null)
            {
                foreach (var rel in task.Relations)
                {
                    AddRelationEdge(adj, rel, task.Id);
                }
            }

            if (task.InverseRelations != null)
            {
                foreach (var rel in task.InverseRelations)
                {
                    AddRelationEdge(adj, rel, task.Id);
                }
            }
        }

        int maxLength = 0;

        foreach (var nodeId in allNodes)
        {
            var visitingPath = new HashSet<int>();
            var memo = new Dictionary<int, int>();
            int chainLength = GetMaxDepthDFS(nodeId, adj, visitingPath, memo);
            if (chainLength > maxLength)
            {
                maxLength = chainLength;
            }
        }

        return maxLength;
    }

    private static void AddRelationEdge(Dictionary<int, HashSet<int>> adj, TaskRelation rel, int currentTaskId)
    {
        int sourceId = rel.SourceTaskId != 0 ? rel.SourceTaskId : currentTaskId;
        int targetId = rel.TargetTaskId != 0 ? rel.TargetTaskId : currentTaskId;

        if (sourceId == targetId) return;

        if (rel.Type == RelationType.Blocks)
        {
            if (!adj.TryGetValue(sourceId, out var set))
            {
                set = new HashSet<int>();
                adj[sourceId] = set;
            }
            set.Add(targetId);
        }
        else if (rel.Type == RelationType.BlockedBy)
        {
            if (!adj.TryGetValue(targetId, out var set))
            {
                set = new HashSet<int>();
                adj[targetId] = set;
            }
            set.Add(sourceId);
        }
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

        int maxChildDepth = 0;
        if (adj.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var neighbor in neighbors)
            {
                if (visitingPath.Contains(neighbor))
                {
                    continue;
                }

                int depth = 1 + GetMaxDepthDFS(neighbor, adj, visitingPath, memo);
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
    public Dictionary<DayOfWeek, double> CalculateWeekdayDistribution(IEnumerable<TaskItem> tasks)
    {
        var result = new Dictionary<DayOfWeek, double>
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
            .ToList();

        if (completedTasks.Count == 0)
        {
            return result;
        }

        var tasksByDate = completedTasks
            .GroupBy(t => t.CompletedDate!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var datesByWeekday = tasksByDate.Keys.GroupBy(d => d.DayOfWeek);

        foreach (var group in datesByWeekday)
        {
            var weekday = group.Key;
            var activeDates = group.ToList();
            int totalTasksForWeekday = activeDates.Sum(d => tasksByDate[d]);
            int activeDaysCount = activeDates.Count;

            if (activeDaysCount > 0)
            {
                result[weekday] = Math.Round((double)totalTasksForWeekday / activeDaysCount, 1);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public DashboardMetricsDto CalculateMetrics(IEnumerable<TaskItem> allTasks, DashboardFilter filter, DateTime? now = null)
    {
        var allList = allTasks.ToList();

        // 1. Фильтрация по временному диапазону
        var dateFiltered = GetTasksForDateRange(allList, filter.DateRange, now);

        // 2. Фильтрация по Scope
        var scopeFiltered = ApplyEntityScope(dateFiltered, filter);

        var dto = new DashboardMetricsDto();

        if (scopeFiltered.Count == 0)
        {
            foreach (DayOfWeek dow in Enum.GetValues(typeof(DayOfWeek)))
            {
                dto.WeekdayAverages[dow] = 0.0;
            }
            return dto;
        }

        // === Summary Grid Metrics ===
        dto.ActivityPercentage = CalculateActivityMetric(scopeFiltered, filter.DateRange, now);

        var topLevelTasks = scopeFiltered.Where(t => !t.IsSubtask).ToList();
        dto.TotalTasksCount = topLevelTasks.Count > 0 ? topLevelTasks.Count : scopeFiltered.Count;
        dto.TotalSubtasksCount = scopeFiltered.Sum(t => t.Subtasks != null ? GetSubtasksCountRecursive(t) : 0);

        int activeTasksCount = scopeFiltered.Count(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Irrelevant);
        int totalForCompletion = scopeFiltered.Count;
        dto.CompletionRatePercentage = totalForCompletion > 0
            ? Math.Round(((double)activeTasksCount / totalForCompletion) * 100.0, 1)
            : 0.0;

        dto.LongestDependencyChainLength = CalculateLongestDependencyChain(scopeFiltered);

        if (filter.DateRange != DateRangeMode.AllTime)
        {
            var currentDate = (now ?? DateTime.UtcNow).Date;
            int daysWindow = filter.DateRange == DateRangeMode.Recent ? 14 : 90;
            var thresholdDate = currentDate.AddDays(-daysWindow);
            dto.CreatedTasksCount = allList.Count(t => t.CreatedDate.Date >= thresholdDate);
        }
        else
        {
            dto.CreatedTasksCount = null;
        }

        // === Task Structure & Types ===
        dto.StatusDistribution = scopeFiltered
            .GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        dto.DateSourceDistribution = scopeFiltered
            .GroupBy(t => t.DateSource)
            .ToDictionary(g => g.Key, g => g.Count());

        dto.TagsDistribution = scopeFiltered
            .SelectMany(t => t.Tags ?? [])
            .Where(tt => tt.Tag != null)
            .GroupBy(tt => tt.Tag!.Name)
            .ToDictionary(g => g.Key, g => g.Count());

        dto.ConditionsDistribution = scopeFiltered
            .SelectMany(t => t.Conditions ?? [])
            .Where(tc => tc.Condition != null)
            .GroupBy(tc => tc.Condition!.Name)
            .ToDictionary(g => g.Key, g => g.Count());

        // === Deep Analytics Grid ===
        dto.FilteredCount = scopeFiltered.Count;
        dto.FilteredTotalTimeMinutes = scopeFiltered.Sum(t => t.EstimatedMinutes ?? 0);

        var tasksWithTime = scopeFiltered.Where(t => t.EstimatedMinutes.HasValue).Select(t => t.EstimatedMinutes!.Value).ToList();
        if (tasksWithTime.Count > 0)
        {
            dto.FilteredAvgTimeMinutes = Math.Round(tasksWithTime.Average(), 1);
            dto.FilteredMinTimeMinutes = tasksWithTime.Min();
            dto.FilteredMaxTimeMinutes = tasksWithTime.Max();
        }

        var tasksWithComplexity = scopeFiltered.Where(t => t.Complexity.HasValue).Select(t => t.Complexity!.Value).ToList();
        if (tasksWithComplexity.Count > 0)
        {
            dto.FilteredAvgComplexity = Math.Round(tasksWithComplexity.Average(), 1);
            dto.FilteredMinComplexity = tasksWithComplexity.Min();
            dto.FilteredMaxComplexity = tasksWithComplexity.Max();
        }

        var tasksWithInterest = scopeFiltered.Where(t => t.Interest.HasValue).Select(t => t.Interest!.Value).ToList();
        if (tasksWithInterest.Count > 0)
        {
            dto.FilteredAvgInterest = Math.Round(tasksWithInterest.Average(), 1);
            dto.FilteredMinInterest = tasksWithInterest.Min();
            dto.FilteredMaxInterest = tasksWithInterest.Max();
        }

        // === Weekday Distribution ===
        dto.WeekdayAverages = CalculateWeekdayDistribution(scopeFiltered);

        // === Records & Interest-Priority Analytics ===
        dto.Records = CalculateRecords(scopeFiltered);
        dto.InterestPriorityDistribution = CalculateInterestPriorityDistribution(scopeFiltered);

        return dto;
    }

    private static List<DashboardRecordItem> CalculateRecords(List<TaskItem> tasks)
    {
        var rawRecords = new List<DashboardRecordItem>();
        var completed = tasks.Where(t => t.Status == TaskStatus.Completed && t.CompletedDate.HasValue).ToList();
        var byDate = completed.GroupBy(t => t.CompletedDate!.Value.Date).ToList();

        // 1. Больше всего завершённых задач за день
        if (byDate.Count > 0)
        {
            var maxCountGroup = byDate.OrderByDescending(g => g.Count()).First();
            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Макс. завершённых задач за день",
                Value = $"{maxCountGroup.Count()} задач",
                Date = maxCountGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Макс. завершённых задач за день", Value = "-", Date = null });
        }

        // 2. Больше всего сумма времени в завершённых за день дел
        if (byDate.Count > 0 && byDate.Any(g => g.Sum(t => t.EstimatedMinutes ?? 0) > 0))
        {
            var maxTimeGroup = byDate.OrderByDescending(g => g.Sum(t => t.EstimatedMinutes ?? 0)).First();
            int totalMins = maxTimeGroup.Sum(t => t.EstimatedMinutes ?? 0);
            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Макс. сумма времени за день",
                Value = FormatMinutes(totalMins),
                Date = maxTimeGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Макс. сумма времени за день", Value = "-", Date = null });
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
            double avgInt = minInterestGroup.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value);

            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Мин. интересность за день (≥3 задач)",
                Value = avgInt.ToString("F1"),
                Date = minInterestGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Мин. интересность за день (≥3 задач)", Value = "-", Date = null });
        }

        // 4. Максимальная интересность выполненных за день (>= 3 задач)
        if (eligibleInterestGroups.Count > 0)
        {
            var maxInterestGroup = eligibleInterestGroups
                .OrderByDescending(g => g.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value))
                .First();
            double avgInt = maxInterestGroup.Where(t => t.Interest.HasValue).Average(t => t.Interest!.Value);

            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Макс. интересность за день (≥3 задач)",
                Value = avgInt.ToString("F1"),
                Date = maxInterestGroup.Key
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Макс. интересность за день (≥3 задач)", Value = "-", Date = null });
        }

        // 5. Макс сложность выполненной задачи
        var maxCompTask = completed.Where(t => t.Complexity.HasValue).OrderByDescending(t => t.Complexity!.Value).FirstOrDefault();
        if (maxCompTask != null)
        {
            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Макс. сложность выполненной задачи",
                Value = $"{maxCompTask.Complexity} ({maxCompTask.Title})",
                Date = maxCompTask.CompletedDate?.Date
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Макс. сложность выполненной задачи", Value = "-", Date = null });
        }

        // 6. Макс продолжительность выполненной задачи
        var maxDurTask = completed.Where(t => t.EstimatedMinutes.HasValue).OrderByDescending(t => t.EstimatedMinutes!.Value).FirstOrDefault();
        if (maxDurTask != null)
        {
            rawRecords.Add(new DashboardRecordItem
            {
                Title = "Макс. продолжительность задачи",
                Value = $"{FormatMinutes(maxDurTask.EstimatedMinutes!.Value)} ({maxDurTask.Title})",
                Date = maxDurTask.CompletedDate?.Date
            });
        }
        else
        {
            rawRecords.Add(new DashboardRecordItem { Title = "Макс. продолжительность задачи", Value = "-", Date = null });
        }

        // Сортировка по убыванию даты (если дата null, идут в конец)
        var sortedRecords = rawRecords
            .OrderByDescending(r => r.Date ?? DateTime.MinValue)
            .ToList();

        return sortedRecords;
    }

    private static Dictionary<int, double> CalculateInterestPriorityDistribution(List<TaskItem> tasks)
    {
        var result = new Dictionary<int, double>();

        for (int interest = 1; interest <= 10; interest++)
        {
            var matchingTasks = tasks
                .Where(t => t.Interest == interest && (t.Priority != null || t.PriorityId.HasValue))
                .ToList();

            if (matchingTasks.Count > 0)
            {
                double avgPriority = matchingTasks.Average(t => t.Priority?.Order ?? t.PriorityId ?? 0);
                result[interest] = Math.Round(avgPriority, 1);
            }
            else
            {
                result[interest] = 0.0;
            }
        }

        return result;
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes <= 0) return "0 мин";
        if (minutes < 60) return $"{minutes} мин";
        var hours = minutes / 60;
        var mins = minutes % 60;
        return mins > 0 ? $"{hours} ч {mins} мин" : $"{hours} ч";
    }

    private static int GetSubtasksCountRecursive(TaskItem task)
    {
        if (task.Subtasks == null || task.Subtasks.Count == 0) return 0;
        int count = task.Subtasks.Count;
        foreach (var sub in task.Subtasks)
        {
            count += GetSubtasksCountRecursive(sub);
        }
        return count;
    }
}
