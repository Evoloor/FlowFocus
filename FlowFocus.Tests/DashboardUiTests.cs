using Bunit;
using FlowFocus.Blazor.Pages;
using FlowFocus.Blazor.Components.Dashboard;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class DashboardUiTests : IntegrationTestBase
{
    private readonly BunitContext _ctx;
    private readonly IDashboardAnalyticsService _analyticsService;

    public DashboardUiTests()
    {
        _ctx = new();
        _ctx.Services.AddMudServices();

        _analyticsService = new DashboardAnalyticsService();

        _ctx.Services.AddSingleton<ITaskRepository>(TaskRepo);
        _ctx.Services.AddSingleton<ITagRepository>(TagRepo);
        _ctx.Services.AddSingleton<IExternalConditionRepository>(ConditionRepo);
        _ctx.Services.AddSingleton<IDashboardAnalyticsService>(_analyticsService);
        _ctx.Services.AddSingleton<INotificationService>(NotificationService);
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

        _ctx.Render<MudPopoverProvider>();
    }

    [Fact]
    public void DashboardPage_RendersSuccessfully_WithInitialMetrics()
    {
        var task1 = new TaskItemBuilder().WithId(0).WithTitle("Task 1").WithStatus(TaskStatus.Planned).Build();
        var task2 = new TaskItemBuilder().WithId(0).WithTitle("Task 2").WithStatus(TaskStatus.Completed).Build();
        TaskRepo.Add(task1);
        TaskRepo.Add(task2);

        var cut = _ctx.Render<Dashboard>();

        cut.Markup.Should().Contain("Аналитический Дашборд");
        cut.Markup.Should().Contain("Общее количество");
        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(2);
    }

    [Fact]
    public void DashboardPage_EntityScopeSelect_FiltersActiveTasksAndUpdatesUi()
    {
        var activeTask = new TaskItemBuilder().WithId(0).WithTitle("Active Task").WithStatus(TaskStatus.Planned).Build();
        var completedTask = new TaskItemBuilder().WithId(0).WithTitle("Completed Task").WithStatus(TaskStatus.Completed).Build();
        TaskRepo.Add(activeTask);
        TaskRepo.Add(completedTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(2);
        cut.FindComponents<ActivityMetricCard>().Should().NotBeEmpty();

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Active
        }));

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
        cut.FindComponent<FilteredStatsGrid>().Instance.Metrics.FilteredCount.Should().Be(1);
        cut.FindComponents<ActivityMetricCard>().Should().BeEmpty();
        cut.FindComponents<WeekdayDistributionBarChart>().Should().BeEmpty();

        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Completed
        }));

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
        cut.FindComponents<ActivityMetricCard>().Should().BeEmpty();
    }

    [Fact]
    public void DashboardPage_RendersRecordsAndHistogramCharts()
    {
        var task1 = new TaskItemBuilder().WithId(0).WithTitle("Task 1").WithStatus(TaskStatus.Completed).WithCompletedDate(DateTime.UtcNow).WithInterest(8).Build();
        TaskRepo.Add(task1);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<DashboardRecordsCard>().Should().NotBeEmpty();
        cut.FindComponents<MetricHistogramCard>().Should().NotBeEmpty();
        cut.FindComponents<PriorityHistogramCard>().Should().NotBeEmpty();
    }

    [Fact]
    public void DashboardPage_DateRangeSelect_ShowsCreatedTasksStatCardWhenNotAllTime()
    {
        var recentTask = new TaskItemBuilder()
            .WithId(0)
            .WithTitle("Recent Task")
            .WithScheduledDate(DateTime.UtcNow.AddDays(-2))
            .WithStatus(TaskStatus.Planned)
            .Build();

        TaskRepo.Add(recentTask);

        var cut = _ctx.Render<Dashboard>();

        // Initially AllTime -> CreatedTasksStatCard is hidden
        cut.FindComponents<CreatedTasksStatCard>().Should().BeEmpty();

        // Change DateRange to Recent
        var dateSelect = cut.FindComponent<DateRangeSelect>();
        cut.InvokeAsync(() => dateSelect.Instance.ValueChanged.InvokeAsync(DateRangeMode.Recent));

        // CreatedTasksStatCard is shown
        cut.FindComponents<CreatedTasksStatCard>().Should().NotBeEmpty();
    }

    [Fact]
    public void DashboardPage_EntityScopeSelect_UpdatesCreatedTasksCount()
    {
        var activeRecentTask = new TaskItemBuilder()
            .WithId(0)
            .WithTitle("Active Recent")
            .WithStatus(TaskStatus.Planned)
            .WithScheduledDate(DateTime.UtcNow.AddDays(-2))
            .Build();

        var completedRecentTask = new TaskItemBuilder()
            .WithId(0)
            .WithTitle("Completed Recent")
            .WithStatus(TaskStatus.Completed)
            .WithCompletedDate(DateTime.UtcNow.AddDays(-2))
            .Build();

        TaskRepo.Add(activeRecentTask);
        TaskRepo.Add(completedRecentTask);

        var cut = _ctx.Render<Dashboard>();

        // Enable Recent date range mode
        var dateSelect = cut.FindComponent<DateRangeSelect>();
        cut.InvokeAsync(() => dateSelect.Instance.ValueChanged.InvokeAsync(DateRangeMode.Recent));

        // Scope All -> 2 created tasks
        cut.FindComponent<CreatedTasksStatCard>().Instance.Count.Should().Be(2);

        // Change scope to Active -> 1 created task
        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            DateRange = DateRangeMode.Recent,
            Scope = EntityScopeType.Active
        }));

        cut.FindComponent<CreatedTasksStatCard>().Instance.Count.Should().Be(1);

        // Change scope to Completed -> 1 created task
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            DateRange = DateRangeMode.Recent,
            Scope = EntityScopeType.Completed
        }));

        cut.FindComponent<CreatedTasksStatCard>().Instance.Count.Should().Be(1);
    }

    [Fact]
    public void PieChart_HidesWhenLessThanTwoSlices()
    {
        // Tag pie chart with only 1 tag -> slice count = 1 (< 2)
        Dictionary<string, int> dataSingle = new() { { "Work", 10 } };
        var cutSingle = _ctx.Render<TagsPieChart>(p => p.Add(x => x.Data, dataSingle));
        cutSingle.Markup.Trim().Should().BeEmpty();

        // Tag pie chart with 2 tags -> slice count = 2 (>= 2)
        Dictionary<string, int> dataDouble = new() { { "Work", 10 }, { "Home", 5 } };
        var cutDouble = _ctx.Render<TagsPieChart>(p => p.Add(x => x.Data, dataDouble));
        cutDouble.Markup.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void DistributionPieChart_RendersTitleAndLegendValues()
    {
        Dictionary<string, int> data = new() { { "TagA", 12 }, { "TagB", 8 } };
        var cut = _ctx.Render<DistributionPieChart>(p => p
            .Add(x => x.Title, "Тестовое распределение")
            .Add(x => x.Data, data));

        cut.Find(".chart-title").TextContent.Should().Contain("Тестовое распределение");
        cut.FindAll(".legend-item").Should().HaveCount(2);
        cut.Markup.Should().Contain("12");
        cut.Markup.Should().Contain("8");
        cut.Markup.Should().Contain("60%");
        cut.Markup.Should().Contain("40%");
    }


    [Fact]
    public void DashboardPage_EntityScopeSelect_FiltersByTag_HidesTagChart()
    {
        TagRepo.Add(new() { Name = "Work" });
        TagRepo.Add(new() { Name = "Home" });

        var tagWork = TagRepo.GetByName("Work")!;
        var tagHome = TagRepo.GetByName("Home")!;

        var workTask = new TaskItemBuilder().WithId(0).WithTitle("Work Task").WithStatus(TaskStatus.Planned).Build();
        workTask.Tags.Add(new() { TagId = tagWork.Id });
        TaskRepo.Add(workTask);

        var homeTask = new TaskItemBuilder().WithId(0).WithTitle("Home Task").WithStatus(TaskStatus.Planned).Build();
        homeTask.Tags.Add(new() { TagId = tagHome.Id });
        TaskRepo.Add(homeTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<TagsPieChart>().Should().NotBeEmpty();

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Tag,
            TagId = tagWork.Id
        }));

        cut.FindComponents<TagsPieChart>().Should().BeEmpty();
        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
    }

    [Fact]
    public void DashboardPage_EntityScopeSelect_FiltersByCondition_HidesConditionChart()
    {
        ConditionRepo.Add(new() { Name = "Rainy" });
        var condition = ConditionRepo.GetAll().First(c => c.Name == "Rainy");

        var taskWithCondition = new TaskItemBuilder().WithId(0).WithTitle("Rainy Task").WithStatus(TaskStatus.Planned).Build();
        taskWithCondition.Conditions.Add(new() { ConditionId = condition.Id });
        TaskRepo.Add(taskWithCondition);

        var otherTask = new TaskItemBuilder().WithId(0).WithTitle("Sunny Task").WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(otherTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<ConditionsPieChart>().Should().NotBeEmpty();

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Condition,
            ConditionId = condition.Id
        }));

        cut.FindComponents<ConditionsPieChart>().Should().BeEmpty();
        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
    }

    [Fact]
    public void BlockingChainRecord_DashWhenNoChain_ShowsLinksWhenChainExists()
    {
        var singleTask = new TaskItemBuilder().WithId(0).WithTitle("Single Task").Build();
        TaskRepo.Add(singleTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponent<DashboardRecordsCard>().Instance.Records
            .Single(r => r.Title == "Самая длинная цепочка блокировок")
            .Value.Should().Be("-");

        var taskC = new TaskItemBuilder().WithId(103).WithTitle("Task C").Build();
        var taskB = new TaskItemBuilder().WithId(102).WithTitle("Task B").WithRelation(taskC, RelationType.Blocks).Build();
        var taskA = new TaskItemBuilder().WithId(101).WithTitle("Task A").WithRelation(taskB, RelationType.Blocks).Build();

        TaskRepo.Add(taskC);
        TaskRepo.Add(taskB);
        TaskRepo.Add(taskA);

        var cut2 = _ctx.Render<Dashboard>();

        cut2.FindComponent<DashboardRecordsCard>().Instance.Records
            .Single(r => r.Title == "Самая длинная цепочка блокировок")
            .Value.Should().Be("2 связей");
    }

    [Fact]
    public void MetricHistogramCard_RendersSuccessfully_OnDashboard()
    {
        var task = new TaskItemBuilder()
            .WithId(0)
            .WithTitle("Task with Interest")
            .WithInterest(8)
            .Build();
        TaskRepo.Add(task);

        var cut = _ctx.Render<Dashboard>();

        var histogramCard = cut.FindComponent<MetricHistogramCard>();
        histogramCard.Should().NotBeNull();
        histogramCard.Markup.Should().Contain("Распределение параметров задач");
    }

    [Fact]
    public void MetricHistogramCard_EvaluatesDisabledOptions_WhenDataMissing()
    {
        var metrics = new DashboardMetricsDto
        {
            InterestHistogram = new() { { "8", 2 } },
            ComplexityHistogram = new(), // empty -> disabled
            TimeHistogram = new()        // empty -> disabled
        };

        var cut = _ctx.Render<MetricHistogramCard>(p => p.Add(x => x.Metrics, metrics));

        cut.Instance.IsInterestDisabled.Should().BeFalse();
        cut.Instance.IsComplexityDisabled.Should().BeTrue();
        cut.Instance.IsTimeDisabled.Should().BeTrue();

        cut.Instance.SelectedMetric.Should().Be(HistogramMetricType.Interest);
    }

    [Fact]
    public void PriorityHistogramCard_EvaluatesDisabledOptions_WhenDataMissing()
    {
        var metrics = new DashboardMetricsDto
        {
            PriorityHistogram = new(),         // empty -> disabled
            PriorityInterestHistogram = new() // empty -> disabled
        };

        var cut = _ctx.Render<PriorityHistogramCard>(p => p.Add(x => x.Metrics, metrics));

        cut.Instance.IsPriorityDisabled.Should().BeTrue();
        cut.Instance.IsInterestPriorityDisabled.Should().BeTrue();
    }

    [Fact]
    public void StatusDistributionCard_RendersTitleCountsAndPercentages_InheritingFromBaseEnumDistributionCard()
    {
        var data = new StatusDistribution
        {
            [TaskStatus.Planned] = 3,
            [TaskStatus.Completed] = 1
        };

        var cut = _ctx.Render<StatusDistributionCard>(p => p.Add(x => x.Data, data));

        cut.Find(".chart-title").TextContent.Should().Contain("Распределение по статусам");
        cut.Markup.Should().Contain("Запланирована");
        cut.Markup.Should().Contain("Завершена");
        cut.Markup.Should().Contain("75%");
        cut.Markup.Should().Contain("25%");
    }

    [Fact]
    public void StatusDistributionCard_RendersEmptyState_WhenTotalCountIsZero()
    {
        var data = new StatusDistribution();

        var cut = _ctx.Render<StatusDistributionCard>(p => p.Add(x => x.Data, data));

        cut.Markup.Should().Contain("Нет распределения по статусам");
    }

    [Fact]
    public void DateTypeDistributionCard_RendersTitleCountsAndPercentages_InheritingFromBaseEnumDistributionCard()
    {
        var data = new DateSourceDistribution
        {
            [DateSource.Manual] = 2,
            [DateSource.AutoFixed] = 2
        };

        var cut = _ctx.Render<DateTypeDistributionCard>(p => p.Add(x => x.Data, data));

        cut.Find(".chart-title").TextContent.Should().Contain("Назначение дат");
        cut.Markup.Should().Contain("Manual (Ручная)");
        cut.Markup.Should().Contain("Autofixed (Повтор)");
        cut.Markup.Should().Contain("50%");
    }

    [Fact]
    public void DashboardPage_AggregationModeSelect_SwitchesToTimeModeWithoutHidingCards()
    {
        Tag tag1 = new() { Name = "Work" };
        Tag tag2 = new() { Name = "Home" };
        TagRepo.Add(tag1);
        TagRepo.Add(tag2);
        var savedWork = TagRepo.GetByName("Work")!;
        var savedHome = TagRepo.GetByName("Home")!;

        var t1 = new TaskItemBuilder().WithId(0).WithTitle("Work Done").WithStatus(TaskStatus.Completed).WithEstimatedMinutes(90).WithPriorityId(1).Build();
        t1.Tags.Add(new() { TagId = savedWork.Id });
        var t2 = new TaskItemBuilder().WithId(0).WithTitle("Home Planned").WithStatus(TaskStatus.Planned).WithEstimatedMinutes(30).WithPriorityId(2).Build();
        t2.Tags.Add(new() { TagId = savedHome.Id });

        TaskRepo.Add(t1);
        TaskRepo.Add(t2);

        var cut = _ctx.Render<Dashboard>();

        var priorityCard = cut.FindComponent<PriorityHistogramCard>();
        priorityCard.Markup.Should().Contain("Количество задач");

        var modeSelect = cut.FindComponent<AggregationModeSelect>();
        cut.InvokeAsync(() => modeSelect.Instance.ValueChanged.InvokeAsync(DashboardAggregationMode.TimeMinutes));

        var totalCard = cut.FindComponent<TotalCountStatCard>();
        totalCard.Markup.Should().Contain("Суммарное время");
        totalCard.Markup.Should().Contain("2 ч");
        totalCard.Markup.Should().Contain("2 из 2");

        var completionCard = cut.FindComponent<CompletionRateStatCard>();
        completionCard.Markup.Should().Contain("Выполненное время");
        completionCard.Instance.Value.Should().Be(75.0);

        var tagsChart = cut.FindComponent<TagsPieChart>();
        tagsChart.Markup.Should().Contain("1.5 ч");
        tagsChart.Markup.Should().Contain("0.5 ч");
        tagsChart.Markup.Should().Contain("1 ч 30 мин");

        priorityCard = cut.FindComponent<PriorityHistogramCard>();
        priorityCard.Markup.Should().Contain("Сумма времени");
        priorityCard.Markup.Should().NotContain("Количество задач");
        priorityCard.Markup.Should().Contain("1.5 ч");
        priorityCard.Markup.Should().Contain("0.5 ч");
        priorityCard.Markup.Should().Contain("1 ч 30 мин");

        cut.InvokeAsync(() => modeSelect.Instance.ValueChanged.InvokeAsync(DashboardAggregationMode.TaskCount));
        priorityCard = cut.FindComponent<PriorityHistogramCard>();
        priorityCard.Markup.Should().Contain("Количество задач");
        priorityCard.Markup.Should().NotContain("Сумма времени");
    }

    [Fact]
    public void PriorityHistogramCard_UpdatesLabel_WhenAggregationModeChanges()
    {
        var countMetrics = new DashboardMetricsDto
        {
            AggregationMode = DashboardAggregationMode.TaskCount,
            PriorityHistogram = new() { { "Высокий", 2 } }
        };

        var cut = _ctx.Render<PriorityHistogramCard>(p => p.Add(x => x.Metrics, countMetrics));
        cut.Markup.Should().Contain("Количество задач");
        cut.Markup.Should().NotContain("Сумма времени");

        var timeMetrics = new DashboardMetricsDto
        {
            AggregationMode = DashboardAggregationMode.TimeMinutes,
            PriorityHistogram = new() { { "Высокий", 90 } }
        };

        cut.Render(p => p.Add(x => x.Metrics, timeMetrics));
        cut.Markup.Should().Contain("Сумма времени");
        cut.Markup.Should().NotContain("Количество задач");
        cut.Markup.Should().Contain("1.5 ч");
        cut.Markup.Should().Contain("1 ч 30 мин");
    }

    [Fact]
    public void CompletionRateStatCard_RendersInfoTooltipIcon()
    {
        var cut = _ctx.Render<CompletionRateStatCard>(p => p
            .Add(x => x.Value, 28.9)
            .Add(x => x.AggregationMode, DashboardAggregationMode.TimeMinutes));

        cut.FindComponents<MudTooltip>().Should().ContainSingle();
        cut.FindAll(".kpi-info-icon").Should().ContainSingle();
    }

    [Fact]
    public void DistributionPieChart_InTimeMinutesMode_FormatsLargeHoursCompactly()
    {
        Dictionary<string, int> data = new()
        {
            { "Завершена", 28074 },
            { "Запланирована", 26569 }
        };

        var cut = _ctx.Render<DistributionPieChart>(p => p
            .Add(x => x.Title, "Тест")
            .Add(x => x.Data, data)
            .Add(x => x.AggregationMode, DashboardAggregationMode.TimeMinutes));

        cut.Markup.Should().Contain("468 ч");
        cut.Markup.Should().Contain("443 ч");
        cut.Markup.Should().NotContain("467 ч 54 мин</span>");
        cut.Markup.Should().NotContain(">28074<");
    }

    [Fact]
    public void DashboardPage_UntaggedFilterAndFooter_DisplaysUntaggedStatsSeparatelyFromPieSlices()
    {
        TagRepo.Add(new() { Name = "Work" });
        TagRepo.Add(new() { Name = "Home" });
        var savedWork = TagRepo.GetByName("Work")!;
        var savedHome = TagRepo.GetByName("Home")!;

        var tWork = new TaskItemBuilder().WithId(0).WithTitle("Work Task").WithStatus(TaskStatus.Planned).Build();
        tWork.Tags.Add(new() { TagId = savedWork.Id });
        var tHome = new TaskItemBuilder().WithId(0).WithTitle("Home Task").WithStatus(TaskStatus.Planned).Build();
        tHome.Tags.Add(new() { TagId = savedHome.Id });
        var tUntagged = new TaskItemBuilder().WithId(0).WithTitle("Untagged Task").WithStatus(TaskStatus.Planned).Build();

        TaskRepo.Add(tWork);
        TaskRepo.Add(tHome);
        TaskRepo.Add(tUntagged);

        var cut = _ctx.Render<Dashboard>();

        var tagsChart = cut.FindComponent<TagsPieChart>();
        tagsChart.FindAll(".legend-item").Should().HaveCount(2);
        tagsChart.Markup.Should().Contain("Без тегов:");
        tagsChart.Markup.Should().Contain("33.3%");

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        scopeSelect.Instance.UntaggedTaskCount.Should().Be(1);

        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Tag,
            TagFilterMode = TagFilterMode.Untagged
        }));

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
    }

    [Fact]
    public void DashboardPage_WeeklyComparisonCard_ShowsOnAllScopeAndHidesOnActiveScope()
    {
        var completed = new TaskItemBuilder()
            .WithId(0)
            .WithTitle("Done Recently")
            .WithStatus(TaskStatus.Completed)
            .WithCompletedDate(TodoDay.Today.ToDateTime())
            .Build();
        TaskRepo.Add(completed);

        var cut = _ctx.Render<Dashboard>();
        cut.FindComponents<WeeklyComparisonCard>().Should().NotBeEmpty();
        cut.FindComponent<WeeklyComparisonCard>().Markup.Should().Contain("Динамика за 7 дней");

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new()
        {
            Scope = EntityScopeType.Active
        }));

        cut.FindComponents<WeeklyComparisonCard>().Should().BeEmpty();
    }

    [Fact]
    public void DashboardPage_RecordsCard_DisplaysMaxTransitiveBlocker()
    {
        var leaf = new TaskItemBuilder().WithId(203).WithTitle("Leaf").WithStatus(TaskStatus.Blocked).Build();
        var mid = new TaskItemBuilder().WithId(202).WithTitle("Mid").WithStatus(TaskStatus.Blocked).WithRelation(leaf, RelationType.Blocks).Build();
        var root = new TaskItemBuilder().WithId(201).WithTitle("Root").WithStatus(TaskStatus.Planned).WithRelation(mid, RelationType.Blocks).Build();

        TaskRepo.Add(leaf);
        TaskRepo.Add(mid);
        TaskRepo.Add(root);

        var cut = _ctx.Render<Dashboard>();

        var record = cut.FindComponent<DashboardRecordsCard>().Instance.Records
            .Single(r => r.Title == "Блокирует больше всего задач");
        record.Value.Should().Be("2 задач (Root)");
    }

    [Fact]
    public void TagsPieChart_DoesNotRenderUntaggedFooter_WhenUntaggedTasksCountIsZero()
    {
        Dictionary<string, int> data = new() { { "Work", 5 }, { "Home", 3 } };

        var cut = _ctx.Render<TagsPieChart>(p => p
            .Add(x => x.Data, data)
            .Add(x => x.UntaggedTasksCount, 0));

        cut.Markup.Should().NotContain("Без тегов");
    }
}
