using FlowFocus.Blazor.Helpers;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.AspNetCore.Components;

namespace FlowFocus.Blazor.Components;

/// <summary>
/// Базовый класс для карточек рабочих элементов (TaskCard, SubtaskCard).
/// Инкапсулирует реакцию на настройки приватности, состояние спойлера и единое вычисление CSS-классов.
/// </summary>
public abstract class WorkItemCardBase<T> : ComponentBase, IDisposable where T : WorkItemBase
{
    [Inject] public ISettingsRepository SettingsRepo { get; set; } = null!;
    [Inject] public INotificationService NotificationService { get; set; } = null!;

    protected UserSettings? _settings;
    protected bool _spoilerRevealed;

    protected abstract T WorkItem { get; }

    protected override void OnInitialized()
    {
        _settings = SettingsRepo.GetUserSettings();
        NotificationService.OnSettingsChanged += OnSettingsChanged;
    }

    protected virtual void OnSettingsChanged()
    {
        _settings = SettingsRepo.GetUserSettings();
        InvokeAsync(StateHasChanged);
    }

    protected virtual bool ShouldHideUnderSpoiler =>
        (_settings?.HideTaskTitlesDefault ?? false) && (WorkItem?.HideUnderSpoiler ?? false);

    protected virtual void ToggleSpoiler()
    {
        _spoilerRevealed = !_spoilerRevealed;
    }

    protected virtual string GetCardClass(bool isNested = false, DisplayMode displayMode = DisplayMode.List)
    {
        if (WorkItem == null) return "task-card";
        return WorkItemStyleHelper.GetCardClass(WorkItem, isNested, displayMode);
    }

    protected virtual string GetTitleClass()
    {
        if (WorkItem == null) return string.Empty;
        return WorkItemStyleHelper.GetTitleClass(WorkItem);
    }

    public virtual void Dispose()
    {
        NotificationService.OnSettingsChanged -= OnSettingsChanged;
    }
}
