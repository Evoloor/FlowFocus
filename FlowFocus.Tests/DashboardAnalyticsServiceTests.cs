using FluentAssertions;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Helpers;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using Xunit;

namespace FlowFocus.Tests;

public class DashboardAnalyticsServiceTests
{
    private readonly DashboardAnalyticsService _service = new();

    #region 1. Date Range Filtering Tests

    [Fact]
    public void test_date_range_recent_math_max_Scenario1_DaysDominate()
    {
        var now = new DateTime(2026, 8, 12);
        var tasks = new List<TaskItem>();

        for (int i = 0; i < 150; i++)
        {
            tasks.Add(new TaskItem
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
    public void test_date_range_recent_math_max_Scenario2_TasksDominate()
    {
        var now = new DateTime(2026, 8, 12);
        var tasks = new List<TaskItem>();

        for (int i = 0; i < 20; i++)
        {
            tasks.Add(new TaskItem
            {
                Id = i + 1,
                Title = $"Recent Task {i + 1}",
                CreatedDate = now.AddDays(-5),
                CompletedDate = now.AddDays(-5),
                Status = TaskStatus.Completed
            });
        }

        for (int i = 20; i < 200; i++)
        {
            tasks.Add(new TaskItem
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
    public void test_date_range_representative()
    {
        var now = new DateTime(2026, 8, 12);
        var tasks = new List<TaskItem>();

        for (int i = 0; i < 500; i++)
        {
            tasks.Add(new TaskItem
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
    public void test_date_range_filtering_with_last_changes_on()
    {
        var now = new DateTime(2026, 8, 12);
        var oldTaskWithRecentChanges = new TaskItem
        {
            Id = 1,
            Title = "Task with recent changes",
            CreatedDate = now.AddDays(-50),
            LastChangesOn = now.AddDays(-2)
        };

        var tasks = new List<TaskItem> { oldTaskWithRecentChanges };
        var result = _service.GetTasksForDateRange(tasks, DateRangeMode.Recent, now);

        result.Should().Contain(oldTaskWithRecentChanges);
    }

    [Fact]
    public void test_created_tasks_count_metric()
    {
        var now = new DateTime(2026, 8, 12);
        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, CreatedDate = now.AddDays(-2) },
            new TaskItem { Id = 2, CreatedDate = now.AddDays(-5) },
            new TaskItem { Id = 3, CreatedDate = now.AddDays(-40) }
        };

        var filterRecent = new DashboardFilter { DateRange = DateRangeMode.Recent };
        var metricsRecent = _service.CalculateMetrics(tasks, filterRecent, now);

        metricsRecent.CreatedTasksCount.Should().Be(2);

        var filterAllTime = new DashboardFilter { DateRange = DateRangeMode.AllTime };
        var metricsAllTime = _service.CalculateMetrics(tasks, filterAllTime, now);

        metricsAllTime.CreatedTasksCount.Should().BeNull();
    }

    [Fact]
    public void test_entity_scope_filtering()
    {
        var now = new DateTime(2026, 8, 12);

        var workTag = new Tag { Id = 1, Name = "Работа" };
        var homeTag = new Tag { Id = 2, Name = "Дом" };

        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "Work Task",
                CreatedDate = now.AddDays(-2),
                Tags = new List<TaskTag> { new TaskTag { TagId = 1, Tag = workTag } }
            },
            new TaskItem
            {
                Id = 2,
                Title = "Home Task",
                CreatedDate = now.AddDays(-2),
                Tags = new List<TaskTag> { new TaskTag { TagId = 2, Tag = homeTag } }
            }
        };

        var filter = new DashboardFilter
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
    public void test_entity_scope_completed_filtering()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, Title = "Active", Status = TaskStatus.Planned },
            new TaskItem { Id = 2, Title = "Done", Status = TaskStatus.Completed }
        };

        var filter = new DashboardFilter { Scope = EntityScopeType.Completed };
        var result = _service.ApplyEntityScope(tasks, filter);

        result.Should().HaveCount(1);
        result.First().Title.Should().Be("Done");
    }

    #endregion

    #region 2. Summary Grid Metrics Tests

    [Fact]
    public void test_activity_metric_percentage()
    {
        var now = new DateTime(2026, 8, 12);
        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-1) },
            new TaskItem { Id = 2, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-1) },
            new TaskItem { Id = 3, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-3) },
            new TaskItem { Id = 4, Status = TaskStatus.Completed, CompletedDate = now.AddDays(-5) },
        };

        double activity = _service.CalculateActivityMetric(tasks, DateRangeMode.Recent, now);

        activity.Should().Be(Math.Round((3.0 / 14.0) * 100.0, 1));
    }

    [Fact]
    public void test_completion_rate_calculation()
    {
        var tasks = new List<TaskItem>();

        for (int i = 0; i < 20; i++)
        {
            tasks.Add(new TaskItem { Id = i + 1, Status = TaskStatus.Planned });
        }

        for (int i = 20; i < 80; i++)
        {
            tasks.Add(new TaskItem { Id = i + 1, Status = TaskStatus.Completed });
        }

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter());

        metrics.CompletionRatePercentage.Should().Be(25.0);
    }

    [Fact]
    public void test_subtasks_count_aggregation()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "Parent Task",
                Subtasks = new List<TaskItem>
                {
                    new TaskItem { Id = 2, Title = "Subtask 1", ParentTaskId = 1 },
                    new TaskItem
                    {
                        Id = 3,
                        Title = "Subtask 2",
                        ParentTaskId = 1,
                        Subtasks = new List<TaskItem>
                        {
                            new TaskItem { Id = 4, Title = "Nested Subtask 2.1", ParentTaskId = 3 }
                        }
                    }
                }
            }
        };

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter());

        metrics.TotalTasksCount.Should().Be(1);
        metrics.TotalSubtasksCount.Should().Be(3);
    }

    #endregion

    #region 3. Graph Dependency Logic Tests

    [Fact]
    public void test_longest_dependency_chain_simple()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "A",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 2,
                Title = "B",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
                }
            },
            new TaskItem { Id = 3, Title = "C" }
        };

        int chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(2);
    }

    [Fact]
    public void test_longest_dependency_chain_branching()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "A",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks },
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 4, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 2,
                Title = "B",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
                }
            },
            new TaskItem { Id = 3, Title = "C" },
            new TaskItem
            {
                Id = 4,
                Title = "D",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 4, TargetTaskId = 5, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 5,
                Title = "E",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 5, TargetTaskId = 6, Type = RelationType.Blocks }
                }
            },
            new TaskItem { Id = 6, Title = "F" }
        };

        int chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(3);
    }

    [Fact]
    public void test_longest_dependency_chain_with_completed_tasks()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "Completed A",
                Status = TaskStatus.Completed,
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 2,
                Title = "Completed B",
                Status = TaskStatus.Completed,
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
                }
            },
            new TaskItem { Id = 3, Title = "Planned C", Status = TaskStatus.Planned }
        };

        int chain = _service.CalculateLongestDependencyChain(tasks);

        chain.Should().Be(2);
    }

    [Fact]
    public void test_longest_dependency_chain_cycle_protection()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "A",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 2,
                Title = "B",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 2, TargetTaskId = 1, Type = RelationType.Blocks }
                }
            }
        };

        var action = () => _service.CalculateLongestDependencyChain(tasks);

        action.Should().NotThrow();
        action().Should().Be(1);
    }

    #endregion

    #region 4. Deep Analytics & Distribution Tests

    [Fact]
    public void test_weekday_distribution_excluding_empty_days()
    {
        var monday1 = new DateTime(2026, 8, 10);
        var monday2 = new DateTime(2026, 8, 3);

        var tasks = new List<TaskItem>();

        for (int i = 0; i < 4; i++)
        {
            tasks.Add(new TaskItem { Id = i + 1, Status = TaskStatus.Completed, CompletedDate = monday1 });
            tasks.Add(new TaskItem { Id = i + 5, Status = TaskStatus.Completed, CompletedDate = monday2 });
        }

        var distribution = _service.CalculateWeekdayDistribution(tasks);

        distribution[DayOfWeek.Monday].Should().Be(4.0);
    }

    [Fact]
    public void test_filtered_stats_null_and_max_value_handling()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, EstimatedMinutes = 10, Complexity = 5, Interest = 8 },
            new TaskItem { Id = 2, EstimatedMinutes = 40, Complexity = 15, Interest = 2 },
            new TaskItem { Id = 3, EstimatedMinutes = null, Complexity = null, Interest = null },
            new TaskItem { Id = 4, EstimatedMinutes = null, Complexity = null, Interest = 6 }
        };

        var filter = new DashboardFilter { Scope = EntityScopeType.All };
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
    public void test_filtered_stats_priority_textual_metrics()
    {
        var critical = new PriorityLevel { Id = 1, Order = 1, Name = "Критический" };
        var high = new PriorityLevel { Id = 2, Order = 2, Name = "Высокий" };
        var low = new PriorityLevel { Id = 3, Order = 5, Name = "Низкий" };

        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, Priority = critical, PriorityId = critical.Id },
            new TaskItem { Id = 2, Priority = high, PriorityId = high.Id },
            new TaskItem { Id = 3, Priority = low, PriorityId = low.Id },
            new TaskItem { Id = 4, Priority = null, PriorityId = null }
        };

        var filter = new DashboardFilter { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.FilteredMaxPriority.Should().Be("Критический");
        metrics.FilteredMinPriority.Should().Be("Низкий");
        metrics.FilteredAvgPriority.Should().Be("Высокий");
    }

    [Fact]
    public void test_pie_chart_grouping_helper()
    {
        // Total sum = 100. A = 55 (55%), B = 30 (30%), C = 8 (8%), D = 7 (7%)
        // C (8%) and D (7%) are < 10% and sum to 15 (<= 15%), so they should be grouped into "Другое"
        var source = new Dictionary<string, int>
        {
            { "A", 55 },
            { "B", 30 },
            { "C", 8 },
            { "D", 7 }
        };

        var result = PieChartGroupingHelper.GroupSmallSlices(source, "Другое");

        result.Should().ContainKey("Другое");
        result["Другое"].Should().Be(15);
        result.Should().NotContainKey("C");
        result.Should().NotContainKey("D");
    }

    [Fact]
    public void test_records_calculation_sorted_by_date_descending()
    {
        var dateOld = new DateTime(2026, 7, 1);
        var dateNew = new DateTime(2026, 8, 10);

        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, Status = TaskStatus.Completed, CompletedDate = dateOld, EstimatedMinutes = 60, Complexity = 20, Interest = 5 },
            new TaskItem { Id = 2, Status = TaskStatus.Completed, CompletedDate = dateNew, EstimatedMinutes = 120, Complexity = 50, Interest = 9 }
        };

        var filter = new DashboardFilter { Scope = EntityScopeType.All };
        var metrics = _service.CalculateMetrics(tasks, filter);

        metrics.Records.Should().NotBeEmpty();

        var dates = metrics.Records.Select(r => r.Date ?? DateTime.MinValue).ToList();
        dates.Should().BeInDescendingOrder();
    }

    [Fact]
    public void test_records_contains_longest_blocking_chain()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem
            {
                Id = 1,
                Title = "A",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 1, TargetTaskId = 2, Type = RelationType.Blocks }
                }
            },
            new TaskItem
            {
                Id = 2,
                Title = "B",
                Relations = new List<TaskRelation>
                {
                    new TaskRelation { SourceTaskId = 2, TargetTaskId = 3, Type = RelationType.Blocks }
                }
            },
            new TaskItem { Id = 3, Title = "C" }
        };

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter { Scope = EntityScopeType.All });

        var chainRecord = metrics.Records.Single(r => r.Title == "Самая длинная цепочка блокировок");
        chainRecord.Value.Should().Be("2 связей");
        chainRecord.Date.Should().BeNull();
    }

    [Fact]
    public void test_records_blocking_chain_dash_when_absent()
    {
        var tasks = new List<TaskItem> { new TaskItem { Id = 1, Title = "Solo" } };

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter { Scope = EntityScopeType.All });

        var chainRecord = metrics.Records.Single(r => r.Title == "Самая длинная цепочка блокировок");
        chainRecord.Value.Should().Be("-");
    }

    [Fact]
    public void test_interest_priority_distribution_calculation()
    {
        var priorityHigh = new PriorityLevel { Id = 1, Order = 1, Name = "High" };
        var priorityLow = new PriorityLevel { Id = 2, Order = 5, Name = "Low" };

        var tasks = new List<TaskItem>
        {
            new TaskItem { Id = 1, Interest = 8, Priority = priorityHigh, PriorityId = 1 },
            new TaskItem { Id = 2, Interest = 8, Priority = priorityLow, PriorityId = 2 }
        };

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter());

        metrics.InterestPriorityDistribution.Should().ContainKey(8);
        metrics.InterestPriorityDistribution[8].Should().Be(3.0);
    }

    #endregion

    #region 5. Empty State Tests

    [Fact]
    public void test_empty_state_zero_values()
    {
        var tasks = new List<TaskItem>();

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter());

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
    public void test_empty_state_charts()
    {
        var tasks = new List<TaskItem>();

        var metrics = _service.CalculateMetrics(tasks, new DashboardFilter());

        metrics.StatusDistribution.Should().BeEmpty();
        metrics.DateSourceDistribution.Should().BeEmpty();
        metrics.TagsDistribution.Should().BeEmpty();
        metrics.ConditionsDistribution.Should().BeEmpty();
        metrics.WeekdayAverages.Values.Should().OnlyContain(v => v == 0.0);
    }

    [Fact]
    public void DistributionHelper_CalculateDistribution_ReturnsCorrectCounts()
    {
        var tasks = new List<TaskItem>
        {
            new TaskItem { Status = TaskStatus.Planned },
            new TaskItem { Status = TaskStatus.Planned },
            new TaskItem { Status = TaskStatus.Completed }
        };

        var result = DistributionHelper.CalculateDistribution(tasks, t => t.Status);

        result[TaskStatus.Planned].Should().Be(2);
        result[TaskStatus.Completed].Should().Be(1);
    }

    [Fact]
    public void DistributionHelper_CalculateCollectionDistribution_ReturnsCorrectCounts()
    {
        var tag1 = new Tag { Id = 1, Name = "Work" };
        var tag2 = new Tag { Id = 2, Name = "Home" };

        var tasks = new List<TaskItem>
        {
            new TaskItem { Tags = new List<TaskTag> { new TaskTag { Tag = tag1 }, new TaskTag { Tag = tag2 } } },
            new TaskItem { Tags = new List<TaskTag> { new TaskTag { Tag = tag1 } } }
        };

        var result = DistributionHelper.CalculateCollectionDistribution(tasks, t => t.Tags?.Where(tt => tt.Tag != null).Select(tt => tt.Tag!.Name));

        result["Work"].Should().Be(2);
        result["Home"].Should().Be(1);
    }

    #endregion
}
