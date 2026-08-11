using Bunit;
using FlowFocus.Blazor.Pages;
using FlowFocus.Blazor.Components.Dashboard;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Xunit;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class DashboardUiTests : IntegrationTestBase
{
    private readonly BunitContext _ctx;
    private readonly IDashboardAnalyticsService _analyticsService;

    public DashboardUiTests()
    {
        _ctx = new BunitContext();
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
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new DashboardFilter
        {
            Scope = EntityScopeType.Active
        }));

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
        cut.FindComponent<FilteredStatsGrid>().Instance.Metrics.FilteredCount.Should().Be(1);
        cut.FindComponents<ActivityMetricCard>().Should().BeEmpty();
        cut.FindComponents<WeekdayDistributionBarChart>().Should().BeEmpty();

        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new DashboardFilter
        {
            Scope = EntityScopeType.Completed
        }));

        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
        cut.FindComponents<ActivityMetricCard>().Should().BeEmpty();
    }

    [Fact]
    public void DashboardPage_RendersRecordsAndInterestPriorityCharts()
    {
        var task1 = new TaskItemBuilder().WithId(0).WithTitle("Task 1").WithStatus(TaskStatus.Completed).WithCompletedDate(DateTime.UtcNow).WithInterest(8).Build();
        TaskRepo.Add(task1);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<DashboardRecordsCard>().Should().NotBeEmpty();
        cut.FindComponents<InterestPriorityChart>().Should().NotBeEmpty();
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
    public void PieChart_HidesWhenLessThanTwoSlices()
    {
        // Tag pie chart with only 1 tag -> slice count = 1 (< 2)
        var dataSingle = new Dictionary<string, int> { { "Work", 10 } };
        var cutSingle = _ctx.Render<TagsPieChart>(p => p.Add(x => x.Data, dataSingle));
        cutSingle.Markup.Trim().Should().BeEmpty();

        // Tag pie chart with 2 tags -> slice count = 2 (>= 2)
        var dataDouble = new Dictionary<string, int> { { "Work", 10 }, { "Home", 5 } };
        var cutDouble = _ctx.Render<TagsPieChart>(p => p.Add(x => x.Data, dataDouble));
        cutDouble.Markup.Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void DashboardPage_EntityScopeSelect_FiltersByTag_HidesTagChart()
    {
        TagRepo.Add(new Tag { Name = "Work" });
        TagRepo.Add(new Tag { Name = "Home" });

        var tagWork = TagRepo.GetByName("Work")!;
        var tagHome = TagRepo.GetByName("Home")!;

        var workTask = new TaskItemBuilder().WithId(0).WithTitle("Work Task").WithStatus(TaskStatus.Planned).Build();
        workTask.Tags.Add(new TaskTag { TagId = tagWork.Id });
        TaskRepo.Add(workTask);

        var homeTask = new TaskItemBuilder().WithId(0).WithTitle("Home Task").WithStatus(TaskStatus.Planned).Build();
        homeTask.Tags.Add(new TaskTag { TagId = tagHome.Id });
        TaskRepo.Add(homeTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<TagsPieChart>().Should().NotBeEmpty();

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new DashboardFilter
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
        ConditionRepo.Add(new ExternalCondition { Name = "Rainy" });
        var condition = ConditionRepo.GetAll().First(c => c.Name == "Rainy");

        var taskWithCondition = new TaskItemBuilder().WithId(0).WithTitle("Rainy Task").WithStatus(TaskStatus.Planned).Build();
        taskWithCondition.Conditions.Add(new TaskCondition { ConditionId = condition.Id });
        TaskRepo.Add(taskWithCondition);

        var otherTask = new TaskItemBuilder().WithId(0).WithTitle("Sunny Task").WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(otherTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<ConditionsPieChart>().Should().NotBeEmpty();

        var scopeSelect = cut.FindComponent<EntityScopeSelect>();
        cut.InvokeAsync(() => scopeSelect.Instance.FilterChanged.InvokeAsync(new DashboardFilter
        {
            Scope = EntityScopeType.Condition,
            ConditionId = condition.Id
        }));

        cut.FindComponents<ConditionsPieChart>().Should().BeEmpty();
        cut.FindComponent<TotalCountStatCard>().Instance.TotalTasks.Should().Be(1);
    }

    [Fact]
    public void LongestChainCard_HiddenWhenLengthIsOneOrLess_ShownWhenGreaterThanOne()
    {
        var singleTask = new TaskItemBuilder().WithId(0).WithTitle("Single Task").Build();
        TaskRepo.Add(singleTask);

        var cut = _ctx.Render<Dashboard>();

        cut.FindComponents<LongestChainStatCard>().Should().BeEmpty();

        var taskC = new TaskItemBuilder().WithId(103).WithTitle("Task C").Build();
        var taskB = new TaskItemBuilder().WithId(102).WithTitle("Task B").WithRelation(taskC, RelationType.Blocks).Build();
        var taskA = new TaskItemBuilder().WithId(101).WithTitle("Task A").WithRelation(taskB, RelationType.Blocks).Build();

        TaskRepo.Add(taskC);
        TaskRepo.Add(taskB);
        TaskRepo.Add(taskA);

        var cut2 = _ctx.Render<Dashboard>();

        cut2.FindComponents<LongestChainStatCard>().Should().NotBeEmpty();
        cut2.FindComponent<LongestChainStatCard>().Instance.ChainLength.Should().Be(2);
    }
}
