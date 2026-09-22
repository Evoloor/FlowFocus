using System.Reflection;
using Bunit;
using FlowFocus.Blazor.Components;
using FlowFocus.Blazor.Dialogs;
using FlowFocus.Blazor.EditDialogContents;
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
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Tests;

public class TaskEditDialogTests : IntegrationTestBase
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ITagSessionService _tagSessionService;
    private readonly ISnackbar _snackbar;
    private readonly IMudDialogInstance _mudDialog;
    private readonly IDialogService _dialogService;
    private readonly BunitContext _ctx;

    public TaskEditDialogTests()
    {
        _ctx = new();
        _ctx.Services.AddMudServices();

        _settingsRepo = Substitute.For<ISettingsRepository>();
        _settingsRepo.GetUserSettings().Returns(new UserSettings());

        _tagSessionService = Substitute.For<ITagSessionService>();
        _tagSessionService.GetSuggestedTags(Arg.Any<int>()).Returns([]);
        _snackbar = Substitute.For<ISnackbar>();
        _mudDialog = Substitute.For<IMudDialogInstance>();
        _dialogService = Substitute.For<IDialogService>();

        _ctx.Services.AddSingleton<ITaskRepository>(TaskRepo);
        _ctx.Services.AddSingleton<IPriorityRepository>(PriorityRepo);
        _ctx.Services.AddSingleton<ITagRepository>(TagRepo);
        _ctx.Services.AddSingleton<IExternalConditionRepository>(ConditionRepo);
        _ctx.Services.AddSingleton<ISettingsRepository>(_settingsRepo);
        _ctx.Services.AddSingleton<ITagSessionService>(_tagSessionService);
        _ctx.Services.AddSingleton<IPlannerService>(PlannerService);
        _ctx.Services.AddSingleton<INotificationService>(NotificationService);
        _ctx.Services.AddSingleton<ISnackbar>(_snackbar);
        _ctx.Services.AddSingleton<IDialogService>(_dialogService);
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
        relations.Add(new()
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
        relations.Add(new()
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
        TaskRelation relation = new()
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

    /// <summary>
    /// Test 6: Creating a new task with subtasks via TaskEditDialog.
    /// Subtasks should be bound to parent, have planned status, copy properties, and live-bind ScheduledDate to parent.
    /// </summary>
    [Fact]
    public async Task SaveTask_NewTaskWithSubtasks_CreatesParentAndLiveBoundSubtasks()
    {
        // Arrange
        DateTime initialDate = new(2026, 9, 20);
        var cut = RenderTaskEditDialog(initialTitle: "Родительская задача с подзадачами");
        var dialogInstance = cut.Instance;

        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskInDialog = (TaskItem)taskField!.GetValue(dialogInstance)!;
        taskInDialog.ScheduledDate = initialDate;
        taskInDialog.DateSource = DateSource.AutoFixed;

        var subtasksField = typeof(TaskEditDialog).GetField("_subtasks", BindingFlags.Instance | BindingFlags.NonPublic);
        var subtasks = (List<FlowFocus.Blazor.EditDialogContents.SubtaskDto>)subtasksField!.GetValue(dialogInstance)!;
        subtasks.Add(new()
        {
            Title = "Подзадача 1",
            EstimatedMinutes = 25,
            Complexity = 4,
            Interest = 7,
            IsFavorite = true
        });
        subtasks.Add(new()
        {
            Title = "Подзадача 2",
            EstimatedMinutes = 40,
            Complexity = 8,
            Interest = 3,
            HideUnderSpoiler = true
        });

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var savedParent = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Родительская задача с подзадачами");
        savedParent.Should().NotBeNull();
        savedParent!.ScheduledDate.Should().Be(initialDate);
        savedParent.Subtasks.Should().HaveCount(2);

        var sub1 = savedParent.Subtasks.FirstOrDefault(s => s.Title == "Подзадача 1");
        sub1.Should().NotBeNull();
        sub1!.ParentTaskId.Should().Be(savedParent.Id);
        sub1.Status.Should().Be(TaskStatus.Planned);
        sub1.EstimatedMinutes.Should().Be(25);
        sub1.Complexity.Should().Be(4);
        sub1.Interest.Should().Be(7);
        sub1.IsFavorite.Should().BeTrue();

        var sub2 = savedParent.Subtasks.FirstOrDefault(s => s.Title == "Подзадача 2");
        sub2.Should().NotBeNull();
        sub2!.ParentTaskId.Should().Be(savedParent.Id);
        sub2.Status.Should().Be(TaskStatus.Planned);
        sub2.EstimatedMinutes.Should().Be(40);
        sub2.Complexity.Should().Be(8);
        sub2.Interest.Should().Be(3);
        sub2.HideUnderSpoiler.Should().BeTrue();
    }

    /// <summary>
    /// Test 7: Updating an existing task by adding a new subtask via TaskEditDialog.
    /// </summary>
    [Fact]
    public async Task SaveTask_EditExistingTask_AddNewSubtask_SavesSubtaskAndLiveBindsDate()
    {
        // Arrange
        DateTime initialDate = new(2026, 9, 10);
        var existing = new TaskItemBuilder()
            .WithId(100)
            .WithTitle("Существующая задача")
            .WithScheduledDate(initialDate, DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(existing);

        var dialogTask = TaskRepo.GetById(100)!;
        var cut = RenderTaskEditDialog(existingTask: dialogTask);
        var dialogInstance = cut.Instance;

        // Add subtask via private field _subtasks
        var subtasksField = typeof(TaskEditDialog).GetField("_subtasks", BindingFlags.Instance | BindingFlags.NonPublic);
        var subtasks = (List<FlowFocus.Blazor.EditDialogContents.SubtaskDto>)subtasksField!.GetValue(dialogInstance)!;
        subtasks.Add(new()
        {
            Title = "Новая добавленная подзадача",
            EstimatedMinutes = 15,
            Complexity = 2
        });

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialogInstance));

        // Assert
        AssertNoSnackbarErrors();
        var updatedParent = TaskRepo.GetById(100);
        updatedParent.Should().NotBeNull();
        updatedParent!.Subtasks.Should().HaveCount(1);

        var addedSubtask = updatedParent.Subtasks.First();
        addedSubtask.Title.Should().Be("Новая добавленная подзадача");
        addedSubtask.ParentTaskId.Should().Be(100);
    }

    [Fact]
    public void TaskDateRow_WhenTaskIsRootTask_RendersScheduledDate()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(400)
            .WithTitle("Обычная задача")
            .WithScheduledDate(new DateTime(2026, 9, 15), DateSource.Manual)
            .Build();

        // Act
        var cut = _ctx.Render<TaskDateRow>(parameters => parameters
            .Add(p => p.Task, task)
            .Add(p => p.Today, TodoDay.Today)
        );

        // Assert
        cut.Markup.Should().Contain("15.09.2026");
    }

    [Fact]
    public void TaskEditDialog_WhenExistingTaskIsAutoFixed_ScheduledDateMirrorIsNull()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(501)
            .WithTitle("Повторяющаяся авто-задача")
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithScheduledDate(new DateTime(2026, 9, 20), DateSource.AutoFixed)
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);

        // Act
        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        var scheduledDateField = typeof(TaskEditDialog).GetField("_scheduledDate", BindingFlags.Instance | BindingFlags.NonPublic);
        var scheduledDate = (DateTime?)scheduledDateField!.GetValue(dialog);

        // Assert: Поле ввода даты должно быть пустым (отображая плейсхолдер "Автоматически")
        scheduledDate.Should().BeNull();
    }

    [Fact]
    public void TaskEditDialog_OnScheduledDateChanged_WhenClearedOnNormalTask_SetsAutoFlexibleAndNull()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(502)
            .WithTitle("Обычная задача с датой")
            .WithScheduledDate(new DateTime(2026, 9, 25), DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);

        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        // Act: Пользователь нажимает на крестик сброса даты
        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [null]);

        var scheduledDateField = typeof(TaskEditDialog).GetField("_scheduledDate", BindingFlags.Instance | BindingFlags.NonPublic);
        var scheduledDate = (DateTime?)scheduledDateField!.GetValue(dialog);

        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskInDialog = (TaskItem)taskField!.GetValue(dialog)!;

        // Assert
        scheduledDate.Should().BeNull();
        taskInDialog.ScheduledDate.Should().BeNull();
        taskInDialog.DateSource.Should().Be(DateSource.AutoFlexible);
    }

    [Fact]
    public void TaskEditDialog_OnScheduledDateChanged_WhenClearedOnRecurringTask_SetsAutoFixedAndNull()
    {
        // Arrange
        var task = new TaskItemBuilder()
            .WithId(503)
            .WithTitle("Повторяющаяся с ручной датой")
            .WithRecurrence(RecurrenceType.EveryN, interval: 1, unit: RecurrenceUnit.Days)
            .WithScheduledDate(new DateTime(2026, 9, 25), DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(task);

        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        // Act: Пользователь сбрасывает дату на крестик
        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [null]);

        var scheduledDateField = typeof(TaskEditDialog).GetField("_scheduledDate", BindingFlags.Instance | BindingFlags.NonPublic);
        var scheduledDate = (DateTime?)scheduledDateField!.GetValue(dialog);

        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskInDialog = (TaskItem)taskField!.GetValue(dialog)!;

        // Assert: статус должен сброситься в AutoFixed, дата null, не откатываясь к Today/Manual
        scheduledDate.Should().BeNull();
        taskInDialog.ScheduledDate.Should().BeNull();
        taskInDialog.DateSource.Should().Be(DateSource.AutoFixed);
    }

    [Fact]
    public async Task TaskEditDialog_OnRecurringChanged_WhenTurnedOnWithAutomaticDate_SetsAutoFixedAndNull()
    {
        // Arrange: новая задача
        var cut = RenderTaskEditDialog(initialTitle: "Новая задача");
        var dialog = cut.Instance;

        // Act: включаем повторение
        var onRecurringMethod = typeof(TaskEditDialog).GetMethod("OnRecurringChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var task = (Task)onRecurringMethod!.Invoke(dialog, [true])!;
        await task;

        var scheduledDateField = typeof(TaskEditDialog).GetField("_scheduledDate", BindingFlags.Instance | BindingFlags.NonPublic);
        var scheduledDate = (DateTime?)scheduledDateField!.GetValue(dialog);

        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var taskInDialog = (TaskItem)taskField!.GetValue(dialog)!;

        // Assert
        scheduledDate.Should().BeNull();
        taskInDialog.ScheduledDate.Should().BeNull();
        taskInDialog.DateSource.Should().Be(DateSource.AutoFixed);
    }

    [Fact]
    public async Task SaveTask_NewRecurringTaskWithAutoDate_NormalizesToTodayAndAutoFixed()
    {
        // Arrange
        var cut = RenderTaskEditDialog(initialTitle: "Новая повторяющаяся задача");
        var dialog = cut.Instance;

        // Включаем повторение
        var onRecurringMethod = typeof(TaskEditDialog).GetMethod("OnRecurringChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var recTask = (Task)onRecurringMethod!.Invoke(dialog, [true])!;
        await recTask;

        // Act: сохраняем задачу
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        AssertNoSnackbarErrors();
        var saved = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Новая повторяющаяся задача");
        saved.Should().NotBeNull();
        saved!.IsRecurring.Should().BeTrue();
        saved.DateSource.Should().Be(DateSource.AutoFixed);
        saved.ScheduledDate.Should().Be(TodoDay.Today.ToDateTime());
    }

    [Fact]
    public async Task SaveTask_WithPastDate_WhenConfirmed_CompletesTaskRetroactively()
    {
        // Arrange
        var pastDate = TodoDay.Today.ToDateTime().AddDays(-2);
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(true));

        var cut = RenderTaskEditDialog(initialTitle: "Задача в прошлом");
        var dialog = cut.Instance;

        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [pastDate]);

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        var saved = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Задача в прошлом");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TaskStatus.Completed);
        saved.CompletedDate.Should().Be(pastDate.Date);
        _mudDialog.Received().Close(Arg.Any<DialogResult>());
    }

    [Fact]
    public async Task SaveTask_WithPastDate_WhenCancelled_DoesNotSaveTask()
    {
        // Arrange
        var pastDate = TodoDay.Today.ToDateTime().AddDays(-2);
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(false));

        var cut = RenderTaskEditDialog(initialTitle: "Несохранённая задача");
        var dialog = cut.Instance;

        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [pastDate]);

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        var saved = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Несохранённая задача");
        saved.Should().BeNull();
        _mudDialog.DidNotReceive().Close(Arg.Any<DialogResult>());
    }

    [Fact]
    public async Task SaveTask_WithTodayDate_DoesNotPromptConfirmation()
    {
        // Arrange
        var today = TodoDay.Today.ToDateTime();
        var cut = RenderTaskEditDialog(initialTitle: "Сегодняшняя задача");
        var dialog = cut.Instance;

        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [today]);

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        await _dialogService.DidNotReceive().ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        );
        var saved = TaskRepo.GetAll().FirstOrDefault(t => t.Title == "Сегодняшняя задача");
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TaskStatus.Planned);
    }

    [Fact]
    public async Task SaveTask_EditExistingTask_WhenDateChangedToPast_PromptsAndCompletes()
    {
        // Arrange
        var existing = new TaskItemBuilder()
            .WithId(200)
            .WithTitle("Существующая задача")
            .WithScheduledDate(TodoDay.Today.ToDateTime(), DateSource.Manual)
            .WithStatus(TaskStatus.Planned)
            .Build();
        TaskRepo.Add(existing);

        var pastDate = TodoDay.Today.ToDateTime().AddDays(-3);
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(true));

        var dialogTask = TaskRepo.GetById(200)!;
        var cut = RenderTaskEditDialog(existingTask: dialogTask);
        var dialog = cut.Instance;

        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [pastDate]);

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        var saved = TaskRepo.GetById(200);
        saved.Should().NotBeNull();
        saved!.Status.Should().Be(TaskStatus.Completed);
        saved.CompletedDate.Should().Be(pastDate.Date);
    }

    [Fact]
    public async Task SaveTask_WithPastDate_WhenRecurringTaskConfirmed_CompletesAndSpawnsNextInstance()
    {
        // Arrange
        var pastDate = TodoDay.Today.Yesterday.ToDateTime();
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(true));

        var cut = RenderTaskEditDialog(initialTitle: "Ежедневная задача в прошлом");
        var dialog = cut.Instance;

        // Включаем повторение
        var onRecurringMethod = typeof(TaskEditDialog).GetMethod("OnRecurringChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        var recTask = (Task)onRecurringMethod!.Invoke(dialog, [true])!;
        await recTask;

        var onDateChangedMethod = typeof(TaskEditDialog).GetMethod("OnScheduledDateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        onDateChangedMethod!.Invoke(dialog, [pastDate]);

        // Act
        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert
        var allTasks = TaskRepo.GetAll().ToList();
        var completed = allTasks.FirstOrDefault(t => t.Title == "Ежедневная задача в прошлом" && t.Status == TaskStatus.Completed);
        completed.Should().NotBeNull();
        completed!.CompletedDate.Should().Be(pastDate.Date);

        var nextInstance = allTasks.FirstOrDefault(t => t.RecurrenceSourceId == completed.Id);
        nextInstance.Should().NotBeNull();
        nextInstance!.Status.Should().Be(TaskStatus.Planned);
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
