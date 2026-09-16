using FlowFocus.Blazor.Dialogs;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Blazor.Components;

public partial class TaskCard : WorkItemCardBase<TaskItem>
{
    [Inject] public ITaskRepository TaskRepo { get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;
    [Inject] public IPlannerService PlannerService { get; set; } = null!;

    [Parameter, EditorRequired]
    public TaskItem Task { get; set; } = null!;

    protected override TaskItem WorkItem => Task;

    [Parameter]
    public DisplayMode DisplayMode { get; set; } = DisplayMode.List;

    [Parameter]
    public bool IsNested { get; set; }

    [Parameter]
    public EventCallback OnTaskChanged { get; set; }

    private string GetCardClass() => GetCardClass(IsNested, DisplayMode);

    private string GetCardStyle()
    {
        List<string> styles = [];

        if (Task.Status == TaskStatus.Blocked || Task.IsBlocked)
        {
            var priority = Task.Priority;
            var color = priority?.Color ?? "#808080";
            styles.Add($"border: 3px solid {color} !important");
        }

        return string.Join(";", styles);
    }

    private string GetPriorityIndicatorStyle()
    {
        var priority = Task.Priority;
        var color = priority?.Color ?? "#808080";
        return $"background-color: {color}";
    }

    private async Task OnTaskCompleted(bool completed)
    {
        if (completed)
        {
            var unblockedTasks = TaskRepo.GetTasksUnblockedBy(Task.Id);

            TaskRepo.CompleteTask(Task.Id);

            if (unblockedTasks.Any())
            {
                DialogParameters<UnblockedTasksDialog> parameters = new()
                {
                    { x => x.UnblockedTasks, unblockedTasks }
                };

                await DialogService.ShowAsync<UnblockedTasksDialog>("Задачи разблокированы", parameters);
            }

            Snackbar.Add("Задача выполнена", Severity.Success);
            NotificationService.NotifyTasksChanged();
            await OnTaskChanged.InvokeAsync();
        }
        else
        {
            TaskRepo.ReopenTask(Task.Id);
            PlannerService.UpdateBlockedStatuses();
            Snackbar.Add("Отменено завершение задачи", Severity.Info);
            NotificationService.NotifyTasksChanged();
            await OnTaskChanged.InvokeAsync();
        }
    }

    private async Task ToggleFavorite()
    {
        Task.IsFavorite = !Task.IsFavorite;
        TaskRepo.Update(Task);
        NotificationService.NotifyTasksChanged();
        await OnTaskChanged.InvokeAsync();
    }

    private async Task OpenEditDialog()
    {
        DialogParameters<TaskEditDialog> parameters = new()
        {
            { x => x.ExistingTask, Task }
        };

        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Medium,
            FullWidth = true,
            CloseOnEscapeKey = true
        };

        var dialog = await DialogService.ShowAsync<TaskEditDialog>("Редактировать задачу", parameters, options);
        var result = await dialog.Result;

        if (result is { Canceled: false })
        {
            await OnTaskChanged.InvokeAsync();
        }
    }

    private async Task MarkIrrelevant()
    {
        var confirmed = await DialogService.ShowMessageBox(
            "Подтверждение",
            "Сделать задачу неактуальной?",
            yesText: "Да", cancelText: "Отмена");

        if (confirmed == true)
        {
            TaskRepo.MarkIrrelevant(Task.Id);
            Snackbar.Add("Задача помечена как неактуальная", Severity.Info);
            NotificationService.NotifyTasksChanged();
            await OnTaskChanged.InvokeAsync();
        }
    }

    private async Task RestoreFromIrrelevant()
    {
        TaskRepo.RestoreFromIrrelevant(Task.Id);
        Snackbar.Add("Задача возвращена в актуальные", Severity.Success);
        NotificationService.NotifyTasksChanged();
        await OnTaskChanged.InvokeAsync();
    }

    private async Task DeleteTask()
    {
        var confirmed = await DialogService.ShowMessageBox(
            "Удаление задачи",
            $"Вы уверены, что хотите удалить задачу \"{Task.Title}\"?",
            yesText: "Удалить", cancelText: "Отмена");

        if (confirmed == true)
        {
            TaskRepo.Delete(Task.Id);
            PlannerService.UpdateBlockedStatuses();
            Snackbar.Add("Задача удалена", Severity.Success);
            NotificationService.NotifyTasksChanged();
            await OnTaskChanged.InvokeAsync();
        }
    }
}
