using FluentAssertions;
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
                    new()
                    {
                        Id = 3,
                        Title = "Subtask 2",
                        ParentTaskId = 1,
                        Subtasks = [new() { Id = 4, Title = "Nested Subtask 2.1", ParentTaskId = 3 }]
                    }
                ]
            }
        ];

        var metrics = _service.CalculateMetrics(tasks, new());

        metrics.TotalTasksCount.Should().Be(1);
        metrics.TotalSubtasksCount.Should().Be(3);
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
}


