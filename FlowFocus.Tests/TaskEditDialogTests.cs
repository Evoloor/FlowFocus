using System.Reflection;
using Bunit;
using FlowFocus.Blazor.Dialogs;
using FlowFocus.Blazor.EditDialogContents;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data.Repositories;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class TaskEditDialogTests : IntegrationTestBase
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ITagSessionService _tagSessionService;
    private readonly ISnackbar _snackbar;
    private readonly IMudDialogInstance _mudDialog;
    private readonly BunitContext _ctx;

    public TaskEditDialogTests()
    {
        _ctx = new BunitContext();
        _ctx.Services.AddMudServices();

        _settingsRepo = Substitute.For<ISettingsRepository>();
        _settingsRepo.GetUserSettings().Returns(new UserSettings());

        _tagSessionService = Substitute.For<ITagSessionService>();
        _tagSessionService.GetSuggestedTags(Arg.Any<int>()).Returns([]);
        _snackbar = Substitute.For<ISnackbar>();
        _mudDialog = Substitute.For<IMudDialogInstance>();

        _ctx.Services.AddSingleton<ITaskRepository>(TaskRepo);
        _ctx.Services.AddSingleton<IPriorityRepository>(PriorityRepo);
        _ctx.Services.AddSingleton<ITagRepository>(TagRepo);
        _ctx.Services.AddSingleton<IExternalConditionRepository>(ConditionRepo);
        _ctx.Services.AddSingleton<ISettingsRepository>(_settingsRepo);
        _ctx.Services.AddSingleton<ITagSessionService>(_tagSessionService);
        _ctx.Services.AddSingleton<IPlannerService>(PlannerService);
        _ctx.Services.AddSingleton<INotificationService>(NotificationService);
        _ctx.Services.AddSingleton<ISnackbar>(_snackbar);
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<TaskEditDialog> RenderTaskEditDialog(TaskItem? existingTask = null, string? initialTitle = null)
    {
        return _ctx.Render<TaskEditDialog>(parameters => parameters
            .Add(p => p.ExistingTask, existingTask)
            .Add(p => p.InitialTitle, initialTitle)
            .AddCascadingValue(_mudDialog)
        );
    }

    private static async Task InvokeSaveTaskAsync(TaskEditDialog dialog)
    {
        var method = typeof(TaskEditDialog).GetMethod("SaveTask", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null)
        {
            var task = (Task)method.Invoke(dialog, null)!;
            await task;
        }
    }

    private void AssertNoSnackbarErrors()
    {
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Error);
    }

    /// <summary>
    /// Test 1: Normal saving of a task via TaskEditDialog.
    /// </summary>
    [Fact]
    public async Task SaveTask_NormalNewTask_SavesTaskSuccessfully()
    {
        // Arrange
        var cut = RenderTaskEditDialog(initialTitle: "Обычная задача");
        var dialogInstance = cut.Instance;

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var saved = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Обычная задача");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TaskStatus.Planned);
    }

    /// <summary>
    /// Test 2: Saving task A after adding a relation to task B.
    /// </summary>
    [Fact]
    public async Task SaveTask_TaskAWithRelationToTaskB_SavesRelationSuccessfully()
    {
        // Arrange
        var taskB = new TaskItemBuilder().WithId(2).WithTitle("Задача Б").Build();
        TaskRepo.Add(taskB);

        var cut = RenderTaskEditDialog(initialTitle: "Задача А");
        var dialogInstance = cut.Instance;

        // Add relation to Task B via private field _relations
        var relationsField = typeof(TaskEditDialog).GetField("_relations", BindingFlags.Instance | BindingFlags.NonPublic);
        var relations = (List<RelationDto>)relationsField!.GetValue(dialogInstance)!;
        relations.Add(new RelationDto
        {
            Type = RelationType.RelatedTo,
            TargetTask = taskB
        });

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var savedA = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Задача А");
        savedA.Should().NotBeNull();

        var relationsA = Context.TaskRelations.Where(r => r.SourceTaskId == savedA!.Id || r.TargetTaskId == savedA.Id).ToList();
        relationsA.Should().NotBeEmpty();
    }

    /// <summary>
    /// Test 3: Updating an existing task via TaskEditDialog.
    /// </summary>
    [Fact]
    public async Task SaveTask_EditExistingTask_UpdatesTaskSuccessfully()
    {
        // Arrange
        var existing = new TaskItemBuilder().WithId(10).WithTitle("Старое название").WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(existing);

        var dialogTask = TaskRepo.GetById(10)!;
        var cut = RenderTaskEditDialog(existingTask: dialogTask);
        var dialogInstance = cut.Instance;

        // Change title
        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskInDialog = (TaskItem)taskField!.GetValue(dialogInstance)!;
        taskInDialog.Title = "Новое название";

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var updated = TaskRepo.GetById(10);
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Новое название");
    }

    /// <summary>
    /// Test 4: Updating an existing task A with added relation to task B via TaskEditDialog.
    /// </summary>
    [Fact]
    public async Task SaveTask_EditExistingTaskAWithNewRelationToTaskB_UpdatesSuccessfully()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(10).WithTitle("Задача А").WithStatus(TaskStatus.Planned).Build();
        var taskB = new TaskItemBuilder().WithId(20).WithTitle("Задача Б").WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);

        var dialogTaskA = TaskRepo.GetById(10)!;
        var cut = RenderTaskEditDialog(existingTask: dialogTaskA);
        var dialogInstance = cut.Instance;

        // Add relation to Task B via private field _relations
        var relationsField = typeof(TaskEditDialog).GetField("_relations", BindingFlags.Instance | BindingFlags.NonPublic);
        var relations = (List<RelationDto>)relationsField!.GetValue(dialogInstance)!;
        relations.Add(new RelationDto
        {
            Type = RelationType.RelatedTo,
            TargetTask = taskB
        });

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var updatedA = TaskRepo.GetById(10);
        updatedA.Should().NotBeNull();

        var relationsA = Context.TaskRelations.Where(r => r.SourceTaskId == 10 || r.TargetTaskId == 10).ToList();
        relationsA.Should().NotBeEmpty();
    }

    /// <summary>
    /// Test 5: Link task B to task A in database, open task A in TaskEditDialog, and save task A with no changes.
    /// </summary>
    [Fact]
    public async Task SaveTask_TaskBLinkedToTaskA_SaveTaskAWithNoChanges_SavesSuccessfully()
    {
        // Arrange
        var taskA = new TaskItemBuilder().WithId(10).WithTitle("Задача А").WithStatus(TaskStatus.Planned).Build();
        var taskB = new TaskItemBuilder().WithId(20).WithTitle("Задача Б").WithStatus(TaskStatus.Planned).Build();
        TaskRepo.Add(taskA);
        TaskRepo.Add(taskB);

        // Link Task B to Task A (Source = Task B, Target = Task A)
        var relation = new TaskRelation
        {
            SourceTaskId = taskB.Id,
            TargetTaskId = taskA.Id,
            Type = RelationType.RelatedTo,
            LastChangesOn = DateTime.UtcNow
        };
        Context.TaskRelations.Add(relation);
        Context.SaveChanges();

        // Refresh repository cache so TaskRepo reads the inverse relation from DB
        var refreshMethod = typeof(TaskRepository).GetMethod("RefreshCache", BindingFlags.Instance | BindingFlags.NonPublic);
        refreshMethod?.Invoke(TaskRepo, null);

        // Detach all entities to simulate detached AsNoTracking entities in real app
        Context.ChangeTracker.Clear();

        // Get Task A from repository with navigation properties populated
        var dialogTaskA = TaskRepo.GetById(10)!;
        dialogTaskA.InverseRelations.Should().NotBeEmpty();

        // Render TaskEditDialog for Task A
        var cut = RenderTaskEditDialog(existingTask: dialogTaskA);
        var dialogInstance = cut.Instance;

        // Act: Save Task A with no changes
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();

        var savedA = TaskRepo.GetById(10);
        savedA.Should().NotBeNull();

        var relationsA = Context.TaskRelations.Where(r => r.SourceTaskId == 10 || r.TargetTaskId == 10).ToList();
        relationsA.Should().NotBeEmpty();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ctx.Dispose();
        }
        base.Dispose(disposing);
    }
}
