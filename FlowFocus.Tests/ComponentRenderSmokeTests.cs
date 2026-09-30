using Bunit;
using FlowFocus.Blazor.Components;
using FlowFocus.Blazor.Dialogs;
using FlowFocus.Blazor.Pages;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class ComponentRenderSmokeTests : BunitContext, IAsyncLifetime
{
    private string _dbPath = null!;

    public async Task InitializeAsync()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();

        _dbPath = Path.Combine(Path.GetTempPath(), $"flowfocus_bunit_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<StorageContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        using var dbContext = new StorageContext(options);
        dbContext.Database.Migrate();

        Services.AddDataLayer(_dbPath);
        Services.BuildServiceProvider().InitializeDatabase();

        Render<MudPopoverProvider>();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Home_Page_ShouldRenderWithoutExceptions()
    {
        // Act
        var cut = Render<Home>();

        // Assert
        cut.Markup.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TaskList_TodayTab_ShouldRenderWithoutExceptions()
    {
        // Act
        var cut = Render<TaskList>(parameters => parameters
            .Add(p => p.Title, "Задачи на сегодня")
            .Add(p => p.DefaultFilter, new TaskListFilter { Type = TaskListFilterType.Today })
            .Add(p => p.ShowTitle, true)
            .Add(p => p.ShowSearch, true)
            .Add(p => p.ShowSortDropdown, true)
            .Add(p => p.ShowDisplayMode, true)
            .Add(p => p.ShowTags, true)
            .Add(p => p.ShowFilters, false)
            .Add(p => p.ShowInactiveToggle, true)
            .Add(p => p.ShowPagination, false)
        );

        // Assert
        cut.Markup.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TaskCard_OnTodayTab_ShouldRenderTuneButton()
    {
        var task = new TaskItem
        {
            Id = 1,
            Title = "Тестовая задача на сегодня",
            ScheduledDate = TodoDay.Today.ToDateTime(),
            Status = TaskStatus.Planned,
            Priority = new PriorityLevel { Name = "Высокий", Color = "#ff0000" }
        };

        // Act
        var cut = Render<TaskCard>(parameters => parameters
            .Add(p => p.Task, task)
            .Add(p => p.IsTodayTab, true)
            .Add(p => p.DisplayMode, DisplayMode.List)
        );

        // Assert
        cut.Markup.Should().Contain("Управление задачей");
    }
}
