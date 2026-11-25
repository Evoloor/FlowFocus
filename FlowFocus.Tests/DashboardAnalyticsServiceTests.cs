using FluentAssertions;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Helpers;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class DashboardAnalyticsServiceTests
{
    private readonly DashboardAnalyticsService _service = new();

    #region 1. Date Range Filtering Tests

    [Fact]
    public void GetTasksForDateRange_RecentModeWithDaysDominating_ReturnsAllTasksWithinDaysWindow()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks = [];

        for (var i = 0; i < 150; i++)
        {
            tasks.Add(new()
            {
                Id = i + 1,
                Title = $"Task {i + 1}",
                CreatedDate = now.AddDays(-i % 10),
                Status = TaskStatus.Planned
            });
        }

        var result = _service.GetTasksForDateRange(tasks, DateRangeMode.Recent, now);

        result.Should().HaveCount(150);
    }

    [Fact]
    public void GetTasksForDateRange_RecentModeWithTasksDominating_ReturnsLimitedTasks()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks = [];

        for (var i = 0; i < 20; i++)
        {
            tasks.Add(new()
            {
                Id = i + 1,
                Title = $"Recent Task {i + 1}",
                CreatedDate = now.AddDays(-5),
                CompletedDate = now.AddDays(-5),
                Status = TaskStatus.Completed
            });
        }

        for (var i = 20; i < 200; i++)
        {
            tasks.Add(new()
            {
                Id = i + 1,
                Title = $"Old Task {i + 1}",
                CreatedDate = now.AddDays(-30 - (i - 20)),
                CompletedDate = now.AddDays(-30 - (i - 20)),
                Status = TaskStatus.Completed
            });
        }

        var result = _service.GetTasksForDateRange(tasks, DateRangeMode.Recent, now);

        result.Should().HaveCount(100);
    }

    [Fact]
    public void GetTasksForDateRange_RepresentativeMode_ReturnsRepresentativeSample()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks = [];

        for (var i = 0; i < 500; i++)
        {
            tasks.Add(new()
            {
                Id = i + 1,
                Title = $"Task {i + 1}",
                CreatedDate = now.AddDays(-i),
                CompletedDate = now.AddDays(-i),
                Status = TaskStatus.Completed
            });
        }

        var result = _service.GetTasksForDateRange(tasks, DateRangeMode.Representative, now);

        result.Should().HaveCount(300);
    }

    [Fact]
    public void GetTasksForDateRange_OldTaskWithRecentChanges_IncludesTask()
    {
        DateTime now = new(2026, 8, 12);
        TaskItem oldTaskWithRecentChanges = new()
        {
            Id = 1,
            Title = "Task with recent changes",
            CreatedDate = now.AddDays(-50),
            LastChangesOn = now.AddDays(-2)
        };

        List<TaskItem> tasks = [oldTaskWithRecentChanges];
        var result = _service.GetTasksForDateRange(tasks, DateRangeMode.Recent, now);

        result.Should().Contain(oldTaskWithRecentChanges);
    }

    [Fact]
    public void CalculateMetrics_CreatedTasksCount_ReturnsCountForRecentModeAndNullForAllTime()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks =
        [
            new() { Id = 1, CreatedDate = now.AddDays(-2) },
            new() { Id = 2, CreatedDate = now.AddDays(-5) },
            new() { Id = 3, CreatedDate = now.AddDays(-40) }
        ];

        DashboardFilter filterRecent = new() { DateRange = DateRangeMode.Recent };
        var metricsRecent = _service.CalculateMetrics(tasks, filterRecent, now);

        metricsRecent.CreatedTasksCount.Should().Be(2);

        DashboardFilter filterAllTime = new() { DateRange = DateRangeMode.AllTime };
        var metricsAllTime = _service.CalculateMetrics(tasks, filterAllTime, now);

        metricsAllTime.CreatedTasksCount.Should().BeNull();
    }

    [Fact]
    public void CalculateMetrics_CreatedTasksCount_AppliesEntityScopeCorrectly()
    {
        DateTime now = new(2026, 8, 12);
        Tag tagWork = new() { Id = 1, Name = "Work" };

        List<TaskItem> tasks =
        [
            new() { Id = 1, CreatedDate = now.AddDays(-2), Status = TaskStatus.Planned, Tags = [new() { TagId = 1, Tag = tagWork }] },
            new() { Id = 2, CreatedDate = now.AddDays(-3), Status = TaskStatus.Completed, Tags = [new() { TagId = 1, Tag = tagWork }] },
            new() { Id = 3, CreatedDate = now.AddDays(-4), Status = TaskStatus.Planned, Tags = [] },
            new() { Id = 4, CreatedDate = now.AddDays(-40), Status = TaskStatus.Planned } // outside 14-day window
        ];

        // Scope = All
        var metricsAll = _service.CalculateMetrics(tasks, new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.All }, now);
        metricsAll.CreatedTasksCount.Should().Be(3);

        // Scope = Active
        var metricsActive = _service.CalculateMetrics(tasks, new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.Active }, now);
        metricsActive.CreatedTasksCount.Should().Be(2); // tasks 1 and 3

        // Scope = Completed
        var metricsCompleted = _service.CalculateMetrics(tasks, new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.Completed }, now);
        metricsCompleted.CreatedTasksCount.Should().Be(1); // task 2

        // Scope = Tag
        var metricsTag = _service.CalculateMetrics(tasks, new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.Tag, TagId = 1 }, now);
        metricsTag.CreatedTasksCount.Should().Be(2); // tasks 1 and 2
    }

    [Fact]
    public void CalculateMetrics_CreatedTasksCount_WhenNoMatchingTasks_ReturnsZeroForRecentMode()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks =
        [
            new() { Id = 1, CreatedDate = now.AddDays(-2), Status = TaskStatus.Completed }
        ];

        // Scope = Active, but only Completed task exists -> scopeFiltered is empty
        DashboardFilter filter = new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.Active };
        var metrics = _service.CalculateMetrics(tasks, filter, now);

        metrics.CreatedTasksCount.Should().Be(0);
        metrics.TotalTasksCount.Should().Be(0);
    }

    [Fact]
    public void PrepareDataSlices_ReturnsCorrectSlicesAcrossDimensions()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks =
        [
            new() { Id = 1, CreatedDate = now.AddDays(-2), Status = TaskStatus.Planned },
            new() { Id = 2, CreatedDate = now.AddDays(-5), Status = TaskStatus.Completed, CompletedDate = now.AddDays(-5) },
            new() { Id = 3, CreatedDate = now.AddDays(-50), Status = TaskStatus.Completed, CompletedDate = now.AddDays(-50) }
        ];

        DashboardFilter filter = new() { DateRange = DateRangeMode.Recent, Scope = EntityScopeType.Active };
        var slices = _service.PrepareDataSlices(tasks, filter, now);

        slices.All.Should().HaveCount(3);
        slices.FullyFiltered.Should().HaveCount(1);
        slices.FullyFiltered[0].Id.Should().Be(1);
        slices.CreatedInScope.Should().NotBeNull();
        slices.CreatedInScope.Should().HaveCount(1);
    }

    [Fact]
    public void ApplyEntityScope_TagScopeFilter_ReturnsOnlyMatchingTasks()
    {
        DateTime now = new(2026, 8, 12);

        Tag workTag = new() { Id = 1, Name = "Работа" };
        Tag homeTag = new() { Id = 2, Name = "Дом" };

        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "Work Task",
                CreatedDate = now.AddDays(-2),
                Tags = [new() { TagId = 1, Tag = workTag }]
            },

            new()
            {
                Id = 2,
                Title = "Home Task",
                CreatedDate = now.AddDays(-2),
                Tags = [new() { TagId = 2, Tag = homeTag }]
            }
        ];

        DashboardFilter filter = new()
        {
            DateRange = DateRangeMode.Recent,
            Scope = EntityScopeType.Tag,
            TagId = 1
        };

        var dateFiltered = _service.GetTasksForDateRange(tasks, filter.DateRange, now);
        var scopeFiltered = _service.ApplyEntityScope(dateFiltered, filter);

        scopeFiltered.Should().HaveCount(1);
        scopeFiltered.First().Title.Should().Be("Work Task");
    }

    [Fact]
    public void ApplyEntityScope_CompletedScopeFilter_ReturnsOnlyCompletedTasks()
    {
        List<TaskItem> tasks =
        [
            new() { Id = 1, Title = "Active", Status = TaskStatus.Planned },
            new() { Id = 2, Title = "Done", Status = TaskStatus.Completed }
        ];

        DashboardFilter filter = new() { Scope = EntityScopeType.Completed };
        var result = _service.ApplyEntityScope(tasks, filter);

        result.Should().HaveCount(1);
        result.First().Title.Should().Be("Done");
    }

    #endregion

    #region 2. Summary Grid Metrics Tests

    [Fact]
    public void CalculateActivityMetric_RecentMode_ReturnsCorrectPercentage()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks =
        [
            new() { Id = 1, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-1) },
            new() { Id = 2, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-1) },
            new() { Id = 3, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-3) },
            new() { Id = 4, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-5) }
        ];

        var activity = _service.CalculateActivityMetric(tasks, DateRangeMode.Recent, now);

        activity.Should().Be(Math.Round((3.0 / 14.0) * 100.0, 1));
    }

    [Fact]
    public void CalculateActivityMetric_RecentMode_ExcludesHistoricalTasksOutsideWindow()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks =
        [
            new() { Id = 1, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-2) }
        ];

        for (var i = 0; i < 30; i++)
        {
            tasks.Add(new() { Id = i + 2, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-30 - i) });
        }

        var activity = _service.CalculateActivityMetric(tasks, DateRangeMode.Recent, now);

        // Only 1 date (now-2) is inside the 14-day window [now-14, now]
        activity.Should().Be(Math.Round((1.0 / 14.0) * 100.0, 1));
    }

    [Fact]
    public void CalculateActivityMetric_RecentMode_CapsAt100Percent()
    {
        DateTime now = new(2026, 8, 12);
        List<TaskItem> tasks = [];

        for (var i = 0; i <= 14; i++)
        {
            tasks.Add(new() { Id = i + 1, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-i) });
        }

        var activity = _service.CalculateActivityMetric(tasks, DateRangeMode.Recent, now);

        activity.Should().Be(100.0);
    }

    [Fact]
    public void CalculateMetrics_CompletionRate_ReturnsCorrectPercentage()
    {
        List<TaskItem> tasks = [];

        for (var i = 0; i < 20; i++)
        {
            tasks.Add(new() { Id = i + 1, Status = TaskStatus.Planned });
        }

        for (var i = 20; i < 80; i++)
        {
            tasks.Add(new() { Id = i + 1, Status = TaskStatus.Completed });
        }

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.CompletionRatePercentage.Should().Be(75.0);
    }

    [Fact]
    public void CalculateMetrics_TaskWithNestedSubtasks_AggregatesCountsCorrectly()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "Parent Task",
                Subtasks =
                [
                    new() { Id = 2, Title = "Subtask 1", ParentTaskId = 1 },
                    new() { Id = 3, Title = "Subtask 2", ParentTaskId = 1 }
                ]
            }
        ];

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.TotalTasksCount.Should().Be(1);
        metrics.TotalSubtasksCount.Should().Be(2);
    }

    #endregion

    #region 3. Graph Dependency Logic Tests

    [Fact]
    public void CalculateLongestDependencyChain_SimpleLinearChain_ReturnsChainLength()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "A",
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }]
            },

            new()
            {
                Id = 2,
                Title = "B",
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }]
            },

            new() { Id = 3, Title = "C" }
        ];

        var chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(2);
    }

    [Fact]
    public void CalculateLongestDependencyChain_BranchingGraph_ReturnsLongestChain()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "A",
                Relations =
                [
                    new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
                    new() { SourceTaskId = 1, TargetTaskId = 4, Type = RelationType.Blocks }
                ]
            },

            new()
            {
                Id = 2,
                Title = "B",
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }]
            },

            new() { Id = 3, Title = "C" },
            new()
            {
                Id = 4,
                Title = "D",
                Relations = [new() { SourceTaskId = 4, TargetTaskId = 5, Type = RelationType.Blocks }]
            },

            new()
            {
                Id = 5,
                Title = "E",
                Relations = [new() { SourceTaskId = 5, TargetTaskId = 6, Type = RelationType.Blocks }]
            },

            new() { Id = 6, Title = "F" }
        ];

        var chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(3);
    }

    [Fact]
    public void CalculateLongestDependencyChain_ChainWithCompletedTasks_CalculatesLength()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "Completed A",
                Status = TaskStatus.Completed,
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }]
            },

            new()
            {
                Id = 2,
                Title = "Completed B",
                Status = TaskStatus.Completed,
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }]
            },

            new() { Id = 3, Title = "Planned C", Status = TaskStatus.Planned }
        ];

        var chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(2);
    }

    [Fact]
    public void CalculateLongestDependencyChain_CyclicDependencies_DoesNotThrowAndHandlesCycle()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "A",
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }]
            },

            new()
            {
                Id = 2,
                Title = "B",
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 1, Type = RelationType.Blocks }]
            }
        ];

        var action = () => _service.CalculateLongestDependencyChain(tasks);

        action.Should().NotThrow();
        action().Should().Be(1);
    }

    #endregion

    #region 4. Deep Analytics & Distribution Tests

    [Fact]
    public void CalculateWeekdayDistribution_MultipleWeeksWithEmptyDays_AveragesOnlyActiveDays()
    {
        DateTime monday1 = new(2026, 8, 10);
        DateTime monday2 = new(2026, 8, 3);

        List<TaskItem> tasks = [];

        for (var i = 0; i < 4; i++)
        {
            tasks.Add(new() { Id = i + 1, Status = TaskStatus.Completed, CompletedDate = monday1 });
            tasks.Add(new() { Id = i + 5, Status = TaskStatus.Completed, CompletedDate = monday2 });
        }

        var distribution = _service.CalculateWeekdayDistribution(tasks);

        distribution[DayOfWeek.Monday].Should().Be(4.0);
    }

    [Fact]
    public void CalculateMetrics_TasksWithNullAndNonNullAttributes_CalculatesMinMaxAvgStats()
    {
        List<TaskItem> tasks =
        [
            new() { Id = 1, EstimatedMinutes = 10, Complexity = 5, Interest = 8 },
            new() { Id = 2, EstimatedMinutes = 40, Complexity = 15, Interest = 2 },
            new() { Id = 3, EstimatedMinutes = null, Complexity = null, Interest = null },
            new() { Id = 4, EstimatedMinutes = null, Complexity = null, Interest = 6 }
        ];

        DashboardFilter filter = new() { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.FilteredAvgTimeMinutes.Should().Be(25.0);
        metrics.FilteredMinTimeMinutes.Should().Be(10);
        metrics.FilteredMaxTimeMinutes.Should().Be(40);

        metrics.FilteredAvgComplexity.Should().Be(10.0);
        metrics.FilteredMinComplexity.Should().Be(5);
        metrics.FilteredMaxComplexity.Should().Be(15);

        metrics.FilteredAvgInterest.Should().Be(5.3);
        metrics.FilteredMinInterest.Should().Be(2);
        metrics.FilteredMaxInterest.Should().Be(8);
    }

    [Fact]
    public void CalculateMetrics_TasksWithPriorities_CalculatesTextualMinMaxAvgPriority()
    {
        PriorityLevel critical = new() { Id = 1, Order = 1, Name = "Критический" };
        PriorityLevel high = new() { Id = 2, Order = 2, Name = "Высокий" };
        PriorityLevel low = new() { Id = 3, Order = 5, Name = "Низкий" };

        List<TaskItem> tasks =
        [
            new() { Id = 1, Priority = critical, PriorityId = critical.Id },
            new() { Id = 2, Priority = high, PriorityId = high.Id },
            new() { Id = 3, Priority = low, PriorityId = low.Id },
            new() { Id = 4, Priority = null, PriorityId = null }
        ];

        DashboardFilter filter = new() { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.FilteredMaxPriority.Should().Be("Критический");
        metrics.FilteredMinPriority.Should().Be("Низкий");
        metrics.FilteredAvgPriority.Should().Be("Высокий");
    }

    [Fact]
    public void GroupSmallSlices_MultipleSlicesBelowThreshold_GroupsIntoOtherCategory()
    {
        // Total sum = 100. A = 55 (55%), B = 30 (30%), C = 8 (8%), D = 7 (7%)
        // C (8%) and D (7%) are < 10% (thresholdPercent = 0.10), and there are 2 small items, so they are grouped into "Другое"
        Dictionary<string, int> source = new()
        {
            { "A", 55 },
            { "B", 30 },
            { "C", 8 },
            { "D", 7 }
        };

        var result = PieChartGroupingHelper.GroupSmallSlices(source, "Другое", thresholdPercent: 0.10);

        result.Should().ContainKey("Другое");
        result["Другое"].Should().Be(15);
        result.Should().NotContainKey("C");
        result.Should().NotContainKey("D");
    }

    [Fact]
    public void GroupSmallSlices_SingleSliceBelowThreshold_DoesNotGroupIntoOther()
    {
        // Total sum = 100. A = 80, B = 15, C = 5.
        // Only C (5%) is under threshold (10%). Grouping 1 slice loses specific info without saving legend space.
        Dictionary<string, int> source = new()
        {
            { "A", 80 },
            { "B", 15 },
            { "C", 5 }
        };

        var result = PieChartGroupingHelper.GroupSmallSlices(source, "Другое", thresholdPercent: 0.10);

        result.Should().NotContainKey("Другое");
        result.Should().ContainKey("C");
        result["C"].Should().Be(5);
    }

    [Fact]
    public void GroupSmallSlices_ExceedsMaxSlicesLimit_GroupsTailIntoOtherCategory()
    {
        // 8 categories. Default maxSlices is 6.
        // Top 5 kept (50, 20, 15, 7, 5). Remaining 3 (1, 1, 1) grouped into "Другое" = 3.
        Dictionary<string, int> source = new()
        {
            { "A", 50 },
            { "B", 20 },
            { "C", 15 },
            { "D", 7 },
            { "E", 5 },
            { "F", 1 },
            { "G", 1 },
            { "H", 1 }
        };

        var result = PieChartGroupingHelper.GroupSmallSlices(source, "Другое", maxSlices: 6, thresholdPercent: 0.01);

        result.Count.Should().Be(6);
        result.Should().ContainKey("Другое");
        result["Другое"].Should().Be(3);
        result.Should().NotContainKey("F");
        result.Should().NotContainKey("G");
        result.Should().NotContainKey("H");
    }

    [Fact]
    public void CalculateMetrics_RecordsList_SortsByDateDescending()
    {
        DateTime dateOld = new(2026, 7, 1);
        DateTime dateNew = new(2026, 8, 10);

        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1, Status = TaskStatus.Completed, CompletedDate = dateOld, EstimatedMinutes = 60, Complexity = 20,
                Interest = 5
            },
            new()
            {
                Id = 2, Status = TaskStatus.Completed, CompletedDate = dateNew, EstimatedMinutes = 120, Complexity = 50,
                Interest = 9
            }
        ];

        DashboardFilter filter = new() { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.Records.Should().NotBeEmpty();

        var dates = metrics.Records.Select(r => r.Date ?? DateTime.MinValue).ToList();
        dates.Should().BeInDescendingOrder();
    }

    [Fact]
    public void CalculateMetrics_WithBlockingChain_ContainsLongestChainRecord()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "A",
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }]
            },

            new()
            {
                Id = 2,
                Title = "B",
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }]
            },

            new() { Id = 3, Title = "C" }
        ];

        var metrics = _service.CalculateMetrics(tasks, new() { Scope = EntityScopeType.All });

        var chainRecord = metrics.Records.Single(r => r.Title == "Самая длинная цепочка блокировок");
        chainRecord.Value.Should().Be("2 связей");
        chainRecord.Date.Should().BeNull();
    }

    [Fact]
    public void CalculateMetrics_WithoutBlockingRelations_ContainsDashForChainRecord()
    {
        List<TaskItem> tasks = [new() { Id = 1, Title = "Solo" }];

        var metrics = _service.CalculateMetrics(tasks, new() { Scope = EntityScopeType.All });

        var chainRecord = metrics.Records.Single(r => r.Title == "Самая длинная цепочка блокировок");
        chainRecord.Value.Should().Be("-");
    }

    [Fact]
    public void CalculateMetrics_InterestAndPriority_CalculatesPriorityInterestHistogram()
    {
        PriorityLevel priorityHigh = new() { Id = 1, Order = 1, Name = "Критический", Color = "#FF4444" };
        PriorityLevel priorityLow = new() { Id = 2, Order = 5, Name = "Низкий", Color = "#4CAF50" };

        List<TaskItem> tasks =
        [
            new() { Id = 1, Interest = 9, Priority = priorityHigh, PriorityId = 1 },
            new() { Id = 2, Interest = 7, Priority = priorityHigh, PriorityId = 1 },
            new() { Id = 3, Interest = 4, Priority = priorityLow, PriorityId = 2 }
        ];

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.PriorityInterestHistogram.Should().HaveCount(2);
        // Order 1 (Критический) comes FIRST
        metrics.PriorityInterestHistogram[0].PriorityName.Should().Be("Критический");
        metrics.PriorityInterestHistogram[0].PriorityOrder.Should().Be(1);
        metrics.PriorityInterestHistogram[0].Color.Should().Be("#FF4444");
        metrics.PriorityInterestHistogram[0].AverageInterest.Should().Be(8.0);
        metrics.PriorityInterestHistogram[0].TaskCount.Should().Be(2);

        // Order 5 (Низкий) comes SECOND
        metrics.PriorityInterestHistogram[1].PriorityName.Should().Be("Низкий");
        metrics.PriorityInterestHistogram[1].PriorityOrder.Should().Be(5);
        metrics.PriorityInterestHistogram[1].Color.Should().Be("#4CAF50");
        metrics.PriorityInterestHistogram[1].AverageInterest.Should().Be(4.0);
        metrics.PriorityInterestHistogram[1].TaskCount.Should().Be(1);
    }

    #endregion

    #region 5. Empty State Tests

    [Fact]
    public void CalculateMetrics_EmptyTaskList_ReturnsZeroAndIsEmptyTrue()
    {
        List<TaskItem> tasks = [];

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.ActivityPercentage.Should().Be(0.0);
        metrics.TotalTasksCount.Should().Be(0);
        metrics.TotalSubtasksCount.Should().Be(0);
        metrics.CompletionRatePercentage.Should().Be(0.0);
        metrics.Records.Should().BeEmpty();
        metrics.FilteredCount.Should().Be(0);
        metrics.FilteredAvgTimeMinutes.Should().BeNull();
        metrics.FilteredMinTimeMinutes.Should().BeNull();
        metrics.FilteredMaxTimeMinutes.Should().BeNull();
        metrics.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void CalculateMetrics_EmptyTaskList_ReturnsEmptyChartDistributions()
    {
        List<TaskItem> tasks = [];

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.StatusDistribution.Should().BeEmpty();
        metrics.DateSourceDistribution.Should().BeEmpty();
        metrics.TagsDistribution.Should().BeEmpty();
        metrics.ConditionsDistribution.Should().BeEmpty();
        metrics.WeekdayAverages.Values.Should().OnlyContain(v => v == 0.0);
    }

    [Fact]
    public void CalculateDistribution_ValidTaskList_ReturnsCorrectCounts()
    {
        List<TaskItem> tasks =
        [
            new() { Status = TaskStatus.Planned },
            new() { Status = TaskStatus.Planned },
            new() { Status = TaskStatus.Completed }
        ];

        var result = DistributionHelper.CalculateDistribution(tasks, t => t.Status);

        result[TaskStatus.Planned].Should().Be(2);
        result[TaskStatus.Completed].Should().Be(1);
    }

    [Fact]
    public void CalculateCollectionDistribution_TasksWithMultipleTags_ReturnsCorrectCounts()
    {
        Tag tag1 = new() { Id = 1, Name = "Work" };
        Tag tag2 = new() { Id = 2, Name = "Home" };

        List<TaskItem> tasks =
        [
            new() { Tags = [new() { Tag = tag1 }, new() { Tag = tag2 }] },
            new() { Tags = [new() { Tag = tag1 }] }
        ];

        var result = DistributionHelper.CalculateCollectionDistribution(tasks, t => t.Tags?.Where(tt => tt.Tag != null).Select(tt => tt.Tag!.Name));

        result["Work"].Should().Be(2);
        result["Home"].Should().Be(1);
    }

    [Fact]
    public void BaseEnumDistribution_CalculatesTotalCountAndPercentageCorrectly()
    {
        var statusDist = new StatusDistribution
        {
            [TaskStatus.Planned] = 3,
            [TaskStatus.Completed] = 1
        };

        statusDist.TotalCount.Should().Be(4);
        statusDist.GetCount(TaskStatus.Planned).Should().Be(3);
        statusDist.GetCount(TaskStatus.Completed).Should().Be(1);
        statusDist.GetCount(TaskStatus.Blocked).Should().Be(0);

        statusDist.GetPercentage(TaskStatus.Planned).Should().Be(75.0);
        statusDist.GetPercentage(TaskStatus.Completed).Should().Be(25.0);
        statusDist.GetPercentage(TaskStatus.Blocked).Should().Be(0.0);
    }

    [Fact]
    public void CalculateEnumDistribution_ReturnsStronglyTypedEnumDistributionModel()
    {
        List<TaskItem> tasks =
        [
            new() { Status = TaskStatus.Planned, DateSource = DateSource.Manual },
            new() { Status = TaskStatus.Planned, DateSource = DateSource.AutoFixed },
            new() { Status = TaskStatus.Completed, DateSource = DateSource.AutoFlexible }
        ];

        var statusResult = DistributionHelper.CalculateEnumDistribution<TaskItem, TaskStatus, StatusDistribution>(tasks, t => t.Status);
        statusResult.Should().BeOfType<StatusDistribution>();
        statusResult.TotalCount.Should().Be(3);
        statusResult.GetPercentage(TaskStatus.Planned).Should().Be(66.7);
        statusResult.GetPercentage(TaskStatus.Completed).Should().Be(33.3);

        var dateSourceResult = DistributionHelper.CalculateEnumDistribution<TaskItem, DateSource, DateSourceDistribution>(tasks, t => t.DateSource);
        dateSourceResult.Should().BeOfType<DateSourceDistribution>();
        dateSourceResult.TotalCount.Should().Be(3);
        dateSourceResult.GetCount(DateSource.Manual).Should().Be(1);
        dateSourceResult.GetPercentage(DateSource.Manual).Should().Be(33.3);
    }

    #endregion

    #region Metric Histogram Tests

    [Fact]
    public void CalculateMetrics_CalculatesHistogramsForInterestComplexityPriorityAndTime()
    {
        var priorityHigh = new PriorityLevel { Id = 1, Order = 1, Name = "Высокий" };
        var priorityLow = new PriorityLevel { Id = 2, Order = 2, Name = "Низкий" };

        List<TaskItem> tasks =
        [
            new() { Id = 1, Title = "Task 1", Interest = 8, Complexity = 10, EstimatedMinutes = 15, Priority = priorityHigh },
            new() { Id = 2, Title = "Task 2", Interest = 8, Complexity = 20, EstimatedMinutes = 45, Priority = priorityHigh },
            new() { Id = 3, Title = "Task 3", Interest = 5, Complexity = 10, EstimatedMinutes = 120, Priority = priorityLow }
        ];

        DashboardFilter filter = new() { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        // Interest
        metrics.InterestHistogram.Should().ContainKey("8").WhoseValue.Should().Be(2);
        metrics.InterestHistogram.Should().ContainKey("5").WhoseValue.Should().Be(1);

        // Complexity (3 tasks with <=8 unique values -> exact value grouping)
        metrics.ComplexityHistogram.Should().ContainKey("10").WhoseValue.Should().Be(2);
        metrics.ComplexityHistogram.Should().ContainKey("20").WhoseValue.Should().Be(1);

        // Priority
        metrics.PriorityHistogram.Should().ContainKey("Высокий").WhoseValue.Should().Be(2);
        metrics.PriorityHistogram.Should().ContainKey("Низкий").WhoseValue.Should().Be(1);

        // Time
        metrics.TimeHistogram.Should().ContainKey("≤15 мин").WhoseValue.Should().Be(1);
        metrics.TimeHistogram.Should().ContainKey("31–60 мин").WhoseValue.Should().Be(1);
        metrics.TimeHistogram.Should().ContainKey("1–2 ч").WhoseValue.Should().Be(1);
    }

    [Fact]
    public void CalculateMetrics_ComplexityHistogram_DynamicGroupingWhenManyUniqueValues()
    {
        // 10 tasks with distinct complexity values from 1 to 90
        List<TaskItem> tasks = [];
        for (int i = 1; i <= 10; i++)
        {
            tasks.Add(new TaskItem { Id = i, Title = $"Task {i}", Complexity = i * 9 });
        }

        DashboardFilter filter = new() { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.ComplexityHistogram.Should().NotBeEmpty();
        metrics.ComplexityHistogram.Values.Sum().Should().Be(10);
    }

    [Fact]
    public void CalculateMetrics_EmptyTasks_HistogramDictionariesAreEmpty()
    {
        List<TaskItem> tasks = [];
        DashboardFilter filter = new() { Scope = EntityScopeType.All };

        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.InterestHistogram.Should().BeEmpty();
        metrics.ComplexityHistogram.Should().BeEmpty();
        metrics.PriorityHistogram.Should().BeEmpty();
        metrics.TimeHistogram.Should().BeEmpty();
    }

    #endregion

    #region 6. New Dashboard Improvements Tests

    [Fact]
    public void CalculateMetrics_CompletionRate_CountsOnlyCompletedVsActiveNonRecurring()
    {
        List<TaskItem> tasks =
        [
            new() { Id = 1, Status = TaskStatus.Completed, IsRecurring = false },
            new() { Id = 2, Status = TaskStatus.Completed, IsRecurring = false },
            new() { Id = 3, Status = TaskStatus.Planned, IsRecurring = false },
            new() { Id = 4, Status = TaskStatus.Blocked, IsRecurring = false },
            new() { Id = 5, Status = TaskStatus.Irrelevant, IsRecurring = false },
            new() { Id = 6, Status = TaskStatus.NotConfigured, IsRecurring = false },
            new() { Id = 7, Status = TaskStatus.Completed, IsRecurring = true },
            new() { Id = 8, Status = TaskStatus.Planned, IsRecurring = true }
        ];

        var metrics = _service.CalculateMetrics(tasks, new());

        // Completed (2) / (Completed (2) + Planned (1) + Blocked (1)) = 50.0%
        metrics.CompletionRatePercentage.Should().Be(50.0);
    }

    [Fact]
    public void ApplyEntityScope_UntaggedMode_ReturnsOnlyTasksWithoutTags()
    {
        Tag workTag = new() { Id = 1, Name = "Work" };
        List<TaskItem> tasks =
        [
            new() { Id = 1, Title = "Tagged", Tags = [new() { TagId = 1, Tag = workTag }] },
            new() { Id = 2, Title = "Untagged 1", Tags = [] },
            new() { Id = 3, Title = "Untagged 2", Tags = null! }
        ];

        DashboardFilter filter = new()
        {
            Scope = EntityScopeType.Tag,
            TagFilterMode = TagFilterMode.Untagged
        };

        var result = _service.ApplyEntityScope(tasks, filter);

        result.Select(t => t.Id).Should().BeEquivalentTo([2, 3]);
    }

    [Fact]
    public void CalculateMetrics_CalculatesUntaggedStats_WithoutAddingToTagsDistribution()
    {
        Tag workTag = new() { Id = 1, Name = "Work" };
        List<TaskItem> tasks =
        [
            new() { Id = 1, EstimatedMinutes = 60, Tags = [new() { TagId = 1, Tag = workTag }] },
            new() { Id = 2, EstimatedMinutes = 30, Tags = [] },
            new() { Id = 3, EstimatedMinutes = 30, Tags = [] }
        ];

        var countMetrics = _service.CalculateMetrics(tasks, new() { AggregationMode = DashboardAggregationMode.TaskCount });
        countMetrics.TagsDistribution.Keys.Should().BeEquivalentTo(["Work"]);
        countMetrics.UntaggedTasksCount.Should().Be(2);
        countMetrics.UntaggedTasksValue.Should().Be(2);
        countMetrics.UntaggedTasksPercentage.Should().Be(66.7);

        var timeMetrics = _service.CalculateMetrics(tasks, new() { AggregationMode = DashboardAggregationMode.TimeMinutes });
        timeMetrics.TagsDistribution.Should().ContainKey("Work").WhoseValue.Should().Be(60);
        timeMetrics.UntaggedTasksCount.Should().Be(2);
        timeMetrics.UntaggedTasksValue.Should().Be(60);
        timeMetrics.UntaggedTasksPercentage.Should().Be(50.0);
    }

    [Fact]
    public void CalculateMaxTransitiveBlocker_HandlesDiamondsCyclesAndExcludesInactiveTasks()
    {
        // Diamond: A -> B, A -> C, B -> D, C -> D (all active -> A blocks 3 distinct tasks: B, C, D)
        // Completed E -> F (inactive blocker ignored), Active G -> H (Irrelevant target ignored)
        // Cycle: X <-> Y (active -> each blocks 1)
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "Root A",
                Status = TaskStatus.Planned,
                Relations =
                [
                    new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
                    new() { SourceTaskId = 1, TargetTaskId = 3, Type = RelationType.Blocks }
                ]
            },
            new()
            {
                Id = 2,
                Title = "Mid B",
                Status = TaskStatus.Blocked,
                Relations = [new() { SourceTaskId = 2, TargetTaskId = 4, Type = RelationType.Blocks }]
            },
            new()
            {
                Id = 3,
                Title = "Mid C",
                Status = TaskStatus.Blocked,
                Relations = [new() { SourceTaskId = 3, TargetTaskId = 4, Type = RelationType.Blocks }]
            },
            new() { Id = 4, Title = "Leaf D", Status = TaskStatus.Blocked },
            new()
            {
                Id = 5,
                Title = "Completed E",
                Status = TaskStatus.Completed,
                Relations = [new() { SourceTaskId = 5, TargetTaskId = 6, Type = RelationType.Blocks }]
            },
            new() { Id = 6, Title = "Planned F", Status = TaskStatus.Planned },
            new()
            {
                Id = 7,
                Title = "Planned G",
                Status = TaskStatus.Planned,
                Relations = [new() { SourceTaskId = 7, TargetTaskId = 8, Type = RelationType.Blocks }]
            },
            new() { Id = 8, Title = "Irrelevant H", Status = TaskStatus.Irrelevant },
            new()
            {
                Id = 9,
                Title = "Cycle X",
                Status = TaskStatus.Planned,
                Relations = [new() { SourceTaskId = 9, TargetTaskId = 10, Type = RelationType.Blocks }]
            },
            new()
            {
                Id = 10,
                Title = "Cycle Y",
                Status = TaskStatus.Blocked,
                Relations = [new() { SourceTaskId = 10, TargetTaskId = 9, Type = RelationType.Blocks }]
            }
        ];

        var impact = _service.CalculateMaxTransitiveBlocker(tasks);
        impact.Should().NotBeNull();
        impact!.BlockerTask.Id.Should().Be(1);
        impact.BlockedTasksCount.Should().Be(3);

        var metrics = _service.CalculateMetrics(tasks, new());
        var record = metrics.Records.Single(r => r.Title == "Блокирует больше всего задач");
        record.Value.Should().Be("3 задач (Root A)");
        record.Date.Should().BeNull();
    }

    [Fact]
    public void CalculateMetrics_WithoutActiveBlockers_ContainsDashForMaxBlockerRecord()
    {
        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1,
                Title = "Completed Blocker",
                Status = TaskStatus.Completed,
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }]
            },
            new() { Id = 2, Title = "Planned Target", Status = TaskStatus.Planned }
        ];

        var metrics = _service.CalculateMetrics(tasks, new());
        var record = metrics.Records.Single(r => r.Title == "Блокирует больше всего задач");
        record.Value.Should().Be("-");
    }

    [Fact]
    public void CalculateWeeklyComparison_ComparesLast7DaysWithPrevious7_RespectingScopeAndIgnoringDateRange()
    {
        DateTime today = new(2026, 8, 12);
        Tag workTag = new() { Id = 1, Name = "Work" };
        Tag homeTag = new() { Id = 2, Name = "Home" };

        List<TaskItem> tasks =
        [
            // Current 7 days: [2026-08-06 .. 2026-08-12]
            new()
            {
                Id = 1, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 12),
                EstimatedMinutes = 60, Complexity = 10, Interest = 8,
                Tags = [new() { TagId = 1, Tag = workTag }]
            },
            new()
            {
                Id = 2, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 6),
                EstimatedMinutes = 30, Complexity = 20, Interest = 6,
                Tags = [new() { TagId = 1, Tag = workTag }]
            },
            // Different tag -> excluded by Scope = Tag(1)
            new()
            {
                Id = 3, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 11),
                EstimatedMinutes = 120, Complexity = 90, Interest = 10,
                Tags = [new() { TagId = 2, Tag = homeTag }]
            },
            // Planned -> ignored
            new()
            {
                Id = 4, Status = TaskStatus.Planned, ScheduledDate = new(2026, 8, 12),
                Tags = [new() { TagId = 1, Tag = workTag }]
            },
            // Previous 7 days: [2026-07-30 .. 2026-08-05]
            new()
            {
                Id = 5, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 5),
                EstimatedMinutes = 30, Complexity = 10, Interest = null,
                Tags = [new() { TagId = 1, Tag = workTag }]
            },
            // Too old: 2026-07-29
            new()
            {
                Id = 6, Status = TaskStatus.Completed, CompletedDate = new(2026, 7, 29),
                EstimatedMinutes = 100, Complexity = 50, Interest = 2,
                Tags = [new() { TagId = 1, Tag = workTag }]
            }
        ];

        DashboardFilter filter = new()
        {
            DateRange = DateRangeMode.Recent,
            Scope = EntityScopeType.Tag,
            TagId = 1
        };

        var comparison = _service.CalculateWeeklyComparison(tasks, filter, today);

        comparison.CompletedTasksCount.CurrentValue.Should().Be(2);
        comparison.CompletedTasksCount.PreviousValue.Should().Be(1);
        comparison.CompletedTasksCount.AbsoluteDelta.Should().Be(1);
        comparison.CompletedTasksCount.PercentDelta.Should().Be(100.0);

        comparison.CompletedTimeMinutes.CurrentValue.Should().Be(90);
        comparison.CompletedTimeMinutes.PreviousValue.Should().Be(30);
        comparison.CompletedTimeMinutes.AbsoluteDelta.Should().Be(60);
        comparison.CompletedTimeMinutes.PercentDelta.Should().Be(200.0);

        comparison.AverageComplexity.CurrentValue.Should().Be(15.0);
        comparison.AverageComplexity.PreviousValue.Should().Be(10.0);
        comparison.AverageComplexity.AbsoluteDelta.Should().Be(5.0);
        comparison.AverageComplexity.PercentDelta.Should().Be(50.0);

        comparison.AverageInterest.CurrentValue.Should().Be(7.0);
        comparison.AverageInterest.PreviousValue.Should().BeNull();
        comparison.AverageInterest.AbsoluteDelta.Should().BeNull();
    }

    [Fact]
    public void CalculateMetrics_TimeMinutesMode_AggregatesMetricsByTime()
    {
        DateTime monday = new(2026, 8, 10);
        PriorityLevel high = new() { Id = 1, Order = 1, Name = "Высокий", Color = "#FF8C00" };
        Tag devTag = new() { Id = 1, Name = "Dev" };
        ExternalCondition cond = new() { Id = 1, Name = "Online" };

        List<TaskItem> tasks =
        [
            new()
            {
                Id = 1, Status = TaskStatus.Completed, CompletedDate = monday, CreatedDate = monday,
                DateSource = DateSource.Manual, EstimatedMinutes = 90, Complexity = 20, Interest = 8,
                Priority = high, PriorityId = 1,
                Tags = [new() { TagId = 1, Tag = devTag }],
                Conditions = [new() { ConditionId = 1, Condition = cond }]
            },
            new()
            {
                Id = 2, Status = TaskStatus.Planned, CreatedDate = monday,
                DateSource = DateSource.AutoFlexible, EstimatedMinutes = 30, Complexity = 20, Interest = 6,
                Priority = high, PriorityId = 1
            },
            new()
            {
                Id = 3, Status = TaskStatus.Completed, CompletedDate = monday, CreatedDate = monday,
                DateSource = DateSource.Manual, EstimatedMinutes = null, Complexity = 10, Interest = 4
            }
        ];

        DashboardFilter filter = new()
        {
            DateRange = DateRangeMode.Recent,
            AggregationMode = DashboardAggregationMode.TimeMinutes
        };

        var metrics = _service.CalculateMetrics(tasks, filter, monday);

        metrics.AggregationMode.Should().Be(DashboardAggregationMode.TimeMinutes);
        metrics.TasksWithTimeEstimateCount.Should().Be(2);
        metrics.FilteredTotalTimeMinutes.Should().Be(120);
        metrics.CreatedTasksTimeMinutes.Should().Be(120);

        // Completed time: 90 / (90 + 30) = 75.0%
        metrics.CompletionRatePercentage.Should().Be(75.0);

        metrics.StatusDistribution.GetCount(TaskStatus.Completed).Should().Be(90);
        metrics.StatusDistribution.GetCount(TaskStatus.Planned).Should().Be(30);
        metrics.DateSourceDistribution.GetCount(DateSource.Manual).Should().Be(90);
        metrics.DateSourceDistribution.GetCount(DateSource.AutoFlexible).Should().Be(30);
        metrics.TagsDistribution["Dev"].Should().Be(90);
        metrics.ConditionsDistribution["Online"].Should().Be(90);

        metrics.WeekdayAverages[DayOfWeek.Monday].Should().Be(90.0);
        metrics.InterestHistogram["8"].Should().Be(90);
        metrics.InterestHistogram.Should().NotContainKey("4"); // task 3 has null minutes
        metrics.ComplexityHistogram["20"].Should().Be(120);
        metrics.ComplexityHistogram.Should().NotContainKey("10");
        metrics.PriorityHistogram["Высокий"].Should().Be(120);
        metrics.TimeHistogram["1–2 ч"].Should().Be(90);
        metrics.TimeHistogram["16–30 мин"].Should().Be(30);

        // PriorityInterestHistogram remains average interest
        metrics.PriorityInterestHistogram.Single().AverageInterest.Should().Be(7.0);
    }

    [Fact]
    public void CalculateMetrics_CompletionRate_ReturnsZeroWhenNoEligibleOrNoTimedTasks()
    {
        List<TaskItem> ineligibleTasks =
        [
            new() { Id = 1, Status = TaskStatus.Irrelevant, IsRecurring = false },
            new() { Id = 2, Status = TaskStatus.NotConfigured, IsRecurring = false },
            new() { Id = 3, Status = TaskStatus.Completed, IsRecurring = true },
            new() { Id = 4, Status = TaskStatus.Planned, IsRecurring = true }
        ];

        var metricsIneligible = _service.CalculateMetrics(ineligibleTasks, new());
        metricsIneligible.CompletionRatePercentage.Should().Be(0.0);

        List<TaskItem> untimedEligibleTasks =
        [
            new() { Id = 5, Status = TaskStatus.Completed, IsRecurring = false, EstimatedMinutes = null },
            new() { Id = 6, Status = TaskStatus.Planned, IsRecurring = false, EstimatedMinutes = null }
        ];

        var metricsUntimed = _service.CalculateMetrics(
            untimedEligibleTasks,
            new() { AggregationMode = DashboardAggregationMode.TimeMinutes });
        metricsUntimed.CompletionRatePercentage.Should().Be(0.0);
    }

    [Fact]
    public void CalculateMaxTransitiveBlocker_IgnoresRelatedToBrokenInactiveChainsAndOutOfFilterTargets()
    {
        // RelatedTo must not count as blocking
        List<TaskItem> relatedOnly =
        [
            new()
            {
                Id = 1, Title = "A", Status = TaskStatus.Planned,
                Relations = [new() { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.RelatedTo }]
            },
            new() { Id = 2, Title = "B", Status = TaskStatus.Planned }
        ];
        _service.CalculateMaxTransitiveBlocker(relatedOnly).Should().BeNull();

        // Inactive middle node D breaks chain C -> D -> E
        List<TaskItem> brokenChain =
        [
            new()
            {
                Id = 3, Title = "C", Status = TaskStatus.Planned,
                Relations = [new() { SourceTaskId = 3, TargetTaskId = 4, Type = RelationType.Blocks }]
            },
            new()
            {
                Id = 4, Title = "D", Status = TaskStatus.Completed,
                Relations = [new() { SourceTaskId = 4, TargetTaskId = 5, Type = RelationType.Blocks }]
            },
            new() { Id = 5, Title = "E", Status = TaskStatus.Planned }
        ];
        _service.CalculateMaxTransitiveBlocker(brokenChain).Should().BeNull();

        // Target G outside Tag1 scope -> filtered dashboard metrics should show "-"
        Tag tag1 = new() { Id = 1, Name = "Tag1" };
        Tag tag2 = new() { Id = 2, Name = "Tag2" };
        List<TaskItem> crossScope =
        [
            new()
            {
                Id = 6, Title = "F", Status = TaskStatus.Planned,
                Tags = [new() { TagId = 1, Tag = tag1 }],
                Relations = [new() { SourceTaskId = 6, TargetTaskId = 7, Type = RelationType.Blocks }]
            },
            new()
            {
                Id = 7, Title = "G", Status = TaskStatus.Blocked,
                Tags = [new() { TagId = 2, Tag = tag2 }]
            }
        ];

        var filteredMetrics = _service.CalculateMetrics(
            crossScope,
            new() { Scope = EntityScopeType.Tag, TagFilterMode = TagFilterMode.SpecificTag, TagId = 1 });
        filteredMetrics.Records.Single(r => r.Title == "Блокирует больше всего задач").Value.Should().Be("-");
    }

    [Fact]
    public void CalculateWeeklyComparison_ExcludesFutureDatesAndHandlesZeroPreviousWithoutInfinity()
    {
        DateTime today = new(2026, 8, 12);

        List<TaskItem> tasksWithZeroPrev =
        [
            new() { Id = 1, Status = TaskStatus.Completed, CompletedDate = today, EstimatedMinutes = 20 },
            new() { Id = 2, Status = TaskStatus.Completed, CompletedDate = today.AddDays(-1), EstimatedMinutes = 40 },
            // Future completion date must be excluded
            new() { Id = 3, Status = TaskStatus.Completed, CompletedDate = today.AddDays(1), EstimatedMinutes = 100 }
        ];

        var zeroPrevComparison = _service.CalculateWeeklyComparison(tasksWithZeroPrev, new(), today);
        zeroPrevComparison.CompletedTasksCount.CurrentValue.Should().Be(2);
        zeroPrevComparison.CompletedTasksCount.PreviousValue.Should().Be(0);
        zeroPrevComparison.CompletedTasksCount.AbsoluteDelta.Should().Be(2);
        zeroPrevComparison.CompletedTasksCount.PercentDelta.Should().BeNull();

        zeroPrevComparison.CompletedTimeMinutes.CurrentValue.Should().Be(60);
        zeroPrevComparison.CompletedTimeMinutes.PreviousValue.Should().Be(0);
        zeroPrevComparison.CompletedTimeMinutes.PercentDelta.Should().BeNull();

        // Equal values (1 vs 1) -> AbsoluteDelta == 0, PercentDelta == 0
        List<TaskItem> equalWeeks =
        [
            new() { Id = 10, Status = TaskStatus.Completed, CompletedDate = today },
            new() { Id = 11, Status = TaskStatus.Completed, CompletedDate = today.AddDays(-7) }
        ];
        var equalComparison = _service.CalculateWeeklyComparison(equalWeeks, new(), today);
        equalComparison.CompletedTasksCount.AbsoluteDelta.Should().Be(0);
        equalComparison.CompletedTasksCount.PercentDelta.Should().Be(0);
    }

    [Fact]
    public void CalculateMetrics_TimeMinutesMode_HandlesAllNullEstimatesAndUntimedWeekdaysGracefully()
    {
        Tag workTag = new() { Id = 1, Name = "Work" };
        PriorityLevel high = new() { Id = 1, Order = 1, Name = "Высокий" };
        DateTime monday1 = new(2026, 8, 10);
        DateTime monday2 = new(2026, 8, 3);

        List<TaskItem> allNullEstimates =
        [
            new()
            {
                Id = 1, Status = TaskStatus.Completed, CompletedDate = monday1,
                EstimatedMinutes = null, Complexity = 20, Interest = 7,
                Priority = high, PriorityId = 1,
                Tags = [new() { TagId = 1, Tag = workTag }]
            },
            new()
            {
                Id = 2, Status = TaskStatus.Planned,
                EstimatedMinutes = null, Complexity = 10, Interest = 5,
                Priority = high, PriorityId = 1
            }
        ];

        var emptyTimeMetrics = _service.CalculateMetrics(
            allNullEstimates,
            new() { AggregationMode = DashboardAggregationMode.TimeMinutes });

        emptyTimeMetrics.FilteredTotalTimeMinutes.Should().Be(0);
        emptyTimeMetrics.TasksWithTimeEstimateCount.Should().Be(0);
        emptyTimeMetrics.CompletionRatePercentage.Should().Be(0.0);
        emptyTimeMetrics.TagsDistribution.Should().BeEmpty();
        emptyTimeMetrics.InterestHistogram.Should().BeEmpty();
        emptyTimeMetrics.ComplexityHistogram.Should().BeEmpty();
        emptyTimeMetrics.PriorityHistogram.Should().BeEmpty();
        emptyTimeMetrics.TimeHistogram.Should().BeEmpty();
        emptyTimeMetrics.WeekdayAverages.Values.Should().OnlyContain(v => v == 0.0);

        // Monday 1 has 60 min, Monday 2 has null min -> average should be 60.0 (not 30.0)
        List<TaskItem> mixedWeekdays =
        [
            new() { Id = 3, Status = TaskStatus.Completed, CompletedDate = monday1, EstimatedMinutes = 60 },
            new() { Id = 4, Status = TaskStatus.Completed, CompletedDate = monday2, EstimatedMinutes = null }
        ];
        var weekdayAvg = _service.CalculateWeekdayDistribution(mixedWeekdays, DashboardAggregationMode.TimeMinutes);
        weekdayAvg[DayOfWeek.Monday].Should().Be(60.0);
    }

    [Fact]
    public void DurationFormatHelper_ResolveCompactScaleAndFormatCompact_FormatsAdaptiveScales()
    {
        DurationFormatHelper.ResolveCompactScale([15, 45]).Should().Be(DurationDisplayScale.CompactMinutes);
        DurationFormatHelper.ResolveCompactScale([15, 60]).Should().Be(DurationDisplayScale.CompactHours);
        DurationFormatHelper.ResolveCompactScale(Array.Empty<int>()).Should().Be(DurationDisplayScale.CompactMinutes);

        DurationFormatHelper.FormatCompact(0, DurationDisplayScale.CompactMinutes).Should().Be("0");
        DurationFormatHelper.FormatCompact(45, DurationDisplayScale.CompactMinutes).Should().Be("45 мин");
        DurationFormatHelper.FormatCompact(30, DurationDisplayScale.CompactHours).Should().Be("0.5 ч");
        DurationFormatHelper.FormatCompact(90, DurationDisplayScale.CompactHours).Should().Be("1.5 ч");
        DurationFormatHelper.FormatCompact(120, DurationDisplayScale.CompactHours).Should().Be("2 ч");
        DurationFormatHelper.FormatCompact(2, DurationDisplayScale.CompactHours).Should().Be("<0.1 ч");
        DurationFormatHelper.FormatCompact(6000, DurationDisplayScale.CompactHours).Should().Be("100 ч");
        DurationFormatHelper.FormatCompact(28074, DurationDisplayScale.CompactHours).Should().Be("468 ч");
        DurationFormatHelper.FormatCompact(70936, DurationDisplayScale.CompactHours).Should().Be("1182 ч");
        DurationFormatHelper.FormatCompact(90, DurationDisplayScale.Detailed).Should().Be("1 ч 30 мин");
    }

    #endregion
}

[Collection("StaticState")]
public class DashboardTodoDayAnalyticsTests
{
    private readonly DashboardAnalyticsService _service = new();

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void CalculateMetrics_UsesTodoDayToday_WhenNowIsNull()
    {
        try
        {
            // 02:30 with DayStartHour = 5 means TodoDay.Today is 2026-08-11 (not 2026-08-12)
            TodoDay.Configure(5);
            TodoDay.SetTimeProvider(new FixedTimeProvider(new(2026, 8, 12, 2, 30, 0, TimeSpan.Zero)));

            List<TaskItem> tasks =
            [
                // 2026-08-05 is inside current 7 days [2026-08-05 .. 2026-08-11] for TodoDay 2026-08-11
                new() { Id = 1, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 5), CreatedDate = new(2026, 8, 5) },
                // 2026-07-29 is inside previous 7 days [2026-07-29 .. 2026-08-04] for TodoDay 2026-08-11 (would be excluded on 2026-08-12)
                new() { Id = 2, Status = TaskStatus.Completed, CompletedDate = new(2026, 7, 29), CreatedDate = new(2026, 7, 29) },
                // 2026-07-28 is outside WeeklyComparison, but inside 14-day window [2026-07-28 .. 2026-08-11] for TodoDay 2026-08-11
                new() { Id = 3, Status = TaskStatus.Completed, CompletedDate = new(2026, 7, 28), CreatedDate = new(2026, 7, 28) }
            ];

            var metrics = _service.CalculateMetrics(tasks, new() { DateRange = DateRangeMode.Recent });

            metrics.WeeklyComparison.CompletedTasksCount.CurrentValue.Should().Be(1);
            metrics.WeeklyComparison.CompletedTasksCount.PreviousValue.Should().Be(1);
            metrics.ActivityPercentage.Should().Be(Math.Round((3.0 / 14.0) * 100.0, 1));
        }
        finally
        {
            TodoDay.SetTimeProvider(null);
            TodoDay.Configure(5);
        }
    }

    [Fact]
    public void CalculateMetrics_TodoDayBoundary_SwitchesDayExactlyAtDayStartHour()
    {
        try
        {
            // At 05:00 with DayStartHour = 5, TodoDay.Today switches to 2026-08-12.
            // Thus 2026-08-05 falls in previous 7 days [2026-07-30 .. 2026-08-05], not current [2026-08-06 .. 2026-08-12].
            TodoDay.Configure(5);
            TodoDay.SetTimeProvider(new FixedTimeProvider(new(2026, 8, 12, 5, 0, 0, TimeSpan.Zero)));

            List<TaskItem> tasks =
            [
                new() { Id = 1, Status = TaskStatus.Completed, CompletedDate = new(2026, 8, 5), CreatedDate = new(2026, 8, 5) }
            ];

            var metrics = _service.CalculateMetrics(tasks, new());

            metrics.WeeklyComparison.CompletedTasksCount.CurrentValue.Should().Be(0);
            metrics.WeeklyComparison.CompletedTasksCount.PreviousValue.Should().Be(1);
        }
        finally
        {
            TodoDay.SetTimeProvider(null);
            TodoDay.Configure(5);
        }
    }
}


