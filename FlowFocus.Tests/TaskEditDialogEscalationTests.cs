using System.Reflection;
using Bunit;
using FlowFocus.Blazor.Dialogs;
using FlowFocus.Blazor.EditDialogContents;
using FlowFocus.Blazor.EditDialogContents.Validators;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Tests.Builders;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace FlowFocus.Tests;

public class TaskEditDialogEscalationTests : IntegrationTestBase
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ITagSessionService _tagSessionService;
    private readonly ISnackbar _snackbar;
    private readonly IMudDialogInstance _mudDialog;
    private readonly IDialogService _dialogService;
    private readonly BunitContext _ctx;

    public TaskEditDialogEscalationTests()
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

    [Fact]
    public async Task TaskEditDialog_WhenTaskHasAppliedPriorityEscalation_SavesSuccessfullyWithoutValidationErrors()
    {
        // Arrange: Задача, у которой приоритет уже повысился до High (Order 1), и есть сработавшая эскалация
        var priorities = PriorityRepo.GetAllOrdered();
        var highPriority = priorities[0]; // Наивысший
        var yesterday = TodoDay.Today.AddDays(-1).ToDateTime();

        var task = new TaskItemBuilder()
            .WithId(801)
            .WithTitle("Задача с уже применённым повышением")
            .WithPriorityId(highPriority.Id)
            .Build();

        task.PriorityEscalations.Add(new PriorityEscalation
        {
            Id = 901,
            TaskId = 801,
            TargetPriorityId = highPriority.Id,
            EscalationDate = yesterday,
            IsApplied = true
        });

        TaskRepo.Add(task);

        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        // Act: Пользователь редактирует только описание задачи и нажимает Сохранить
        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var editedTask = (TaskItem)taskField!.GetValue(dialog)!;
        editedTask.Description = "Обновлённое описание";

        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert: Сохранение успешно, нет предупреждений валидации
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Warning);
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Error);

        var savedTask = TaskRepo.GetById(801);
        savedTask.Should().NotBeNull();
        savedTask!.Description.Should().Be("Обновлённое описание");
        savedTask.PriorityEscalations.Should().ContainSingle(e => e.IsApplied && e.TargetPriorityId == highPriority.Id);
    }

    [Fact]
    public async Task TaskEditDialog_WhenManuallyElevatingPriorityAboveUnappliedEscalation_ShowsConfirmationAndRemovesRedundantEscalation()
    {
        // Arrange: Задача с низким приоритетом и неприменённым повышением до Medium
        var priorities = PriorityRepo.GetAllOrdered();
        var highPriority = priorities[0];    // Order 0
        var mediumPriority = priorities[1];  // Order 1
        var lowPriority = priorities[2];     // Order 2
        var futureDate = TodoDay.Today.AddDays(5).ToDateTime();

        var task = new TaskItemBuilder()
            .WithId(802)
            .WithTitle("Задача с неприменённым повышением")
            .WithPriorityId(lowPriority.Id)
            .Build();

        task.PriorityEscalations.Add(new PriorityEscalation
        {
            Id = 902,
            TaskId = 802,
            TargetPriorityId = mediumPriority.Id,
            EscalationDate = futureDate,
            IsApplied = false
        });

        TaskRepo.Add(task);

        // Пользователь подтверждает удаление в диалоге
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(true));

        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        // Act: Пользователь вручную повышает приоритет задачи до High (Order 0, выше чем Medium Order 1)
        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var editedTask = (TaskItem)taskField!.GetValue(dialog)!;
        editedTask.PriorityId = highPriority.Id;

        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert: Был показан диалог подтверждения, и неактуальное правило удалено
        await _dialogService.Received(1).ShowMessageBox(
            Arg.Is<string>(s => s.Contains("Удаление неактуальных повышений")),
            Arg.Is<string>(s => s.Contains(mediumPriority.Name)),
            Arg.Is<string>(s => s == "Продолжить"),
            Arg.Any<string?>(),
            Arg.Is<string?>(s => s == "Отмена"),
            Arg.Any<DialogOptions?>()
        );

        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Warning);

        var savedTask = TaskRepo.GetById(802);
        savedTask.Should().NotBeNull();
        savedTask!.PriorityId.Should().Be(highPriority.Id);
        savedTask.PriorityEscalations.Should().BeEmpty();
    }

    [Fact]
    public async Task TaskEditDialog_WhenManuallyElevatingPriorityAboveUnappliedEscalation_WhenUserCancels_DoesNotSaveAndRetainsEscalation()
    {
        // Arrange
        var priorities = PriorityRepo.GetAllOrdered();
        var highPriority = priorities[0];
        var mediumPriority = priorities[1];
        var lowPriority = priorities[2];
        var futureDate = TodoDay.Today.AddDays(5).ToDateTime();

        var task = new TaskItemBuilder()
            .WithId(803)
            .WithTitle("Задача для отмены")
            .WithPriorityId(lowPriority.Id)
            .Build();

        task.PriorityEscalations.Add(new PriorityEscalation
        {
            Id = 903,
            TaskId = 803,
            TargetPriorityId = mediumPriority.Id,
            EscalationDate = futureDate,
            IsApplied = false
        });

        TaskRepo.Add(task);

        // Пользователь нажимает "Отмена"
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<DialogOptions?>()
        ).Returns(Task.FromResult<bool?>(false));

        var cut = RenderTaskEditDialog(existingTask: task);
        var dialog = cut.Instance;

        // Act: Меняем приоритет на High и нажимаем Сохранить
        var taskField = typeof(TaskEditDialog).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic);
        var editedTask = (TaskItem)taskField!.GetValue(dialog)!;
        editedTask.PriorityId = highPriority.Id;

        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert: Диалог не закрывался, в базе приоритет не поменялся, правило сохранено
        _mudDialog.DidNotReceive().Close(Arg.Any<DialogResult>());

        var savedTask = TaskRepo.GetById(803);
        savedTask!.PriorityId.Should().Be(lowPriority.Id);
        savedTask.PriorityEscalations.Should().ContainSingle(e => e.TargetPriorityId == mediumPriority.Id);
    }

    [Fact]
    public async Task TaskEditDialog_WhenBlockerCascadeEscalatedPriority_SavesWithoutErrors()
    {
        // Arrange: Блокирующая задача B и блокируемая задача A
        var priorities = PriorityRepo.GetAllOrdered();
        var critical = priorities[0]; // Order 0
        var medium = priorities[1];   // Order 1
        var low = priorities[2];      // Order 2

        var blocker = new TaskItemBuilder()
            .WithId(804)
            .WithTitle("Blocker Task")
            .WithPriorityId(low.Id)
            .Build();

        blocker.PriorityEscalations.Add(new PriorityEscalation
        {
            Id = 904,
            TaskId = 804,
            TargetPriorityId = medium.Id,
            EscalationDate = TodoDay.Today.AddDays(3).ToDateTime(),
            IsApplied = false
        });

        var blocked = new TaskItemBuilder()
            .WithId(805)
            .WithTitle("Blocked Task")
            .WithPriorityId(critical.Id)
            .Build();

        TaskRepo.Add(blocker);
        TaskRepo.Add(blocked);
        Context.TaskRelations.Add(new() { SourceTaskId = blocker.Id, TargetTaskId = blocked.Id, Type = RelationType.Blocks });
        Context.SaveChanges();

        // Act 1: Планировщик каскадно повышает приоритет блокера
        PlannerService.NormalizeBlockerPriorities();

        var updatedBlocker = TaskRepo.GetById(804);
        updatedBlocker!.PriorityId.Should().Be(critical.Id);
        // Неприменённое правило до Medium должно быть помечено IsApplied = true
        updatedBlocker.PriorityEscalations.Should().ContainSingle(e => e.IsApplied && e.TargetPriorityId == medium.Id);

        // Act 2: Открываем блокер в TaskEditDialog и сохраняем
        var cut = RenderTaskEditDialog(existingTask: updatedBlocker);
        var dialog = cut.Instance;

        await cut.InvokeAsync(() => InvokeSaveTaskAsync(dialog));

        // Assert: Сохранение прошло без ошибок валидации
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Warning);
        _snackbar.DidNotReceive().Add(Arg.Any<string>(), Severity.Error);
    }

    [Fact]
    public void EscalationValidator_AllowsAppliedEscalationsEvenWithPastDateOrEqualPriority()
    {
        // Arrange
        var priorities = PriorityRepo.GetAllOrdered();
        var high = priorities[0];
        var low = priorities[1];

        var task = new TaskItem { PriorityId = high.Id };

        List<EscalationDto> escalations =
        [
            new()
            {
                Id = 1,
                TargetPriorityId = high.Id,
                EscalationDate = TodoDay.Today.AddDays(-2).ToDateTime(),
                IsApplied = true
            }
        ];

        // Act
        var result = EscalationValidator.ValidateEscalations(escalations, task, priorities);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void EscalationValidator_RejectsUnappliedEscalationsWithPastDateOrLowerEqualPriority()
    {
        // Arrange
        var priorities = PriorityRepo.GetAllOrdered();
        var high = priorities[0];
        var low = priorities[1];

        var task = new TaskItem { PriorityId = high.Id };

        List<EscalationDto> escalations =
        [
            new()
            {
                Id = 1,
                TargetPriorityId = high.Id,
                EscalationDate = TodoDay.Today.AddDays(-2).ToDateTime(),
                IsApplied = false
            }
        ];

        // Act
        var result = EscalationValidator.ValidateEscalations(escalations, task, priorities);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }
}
