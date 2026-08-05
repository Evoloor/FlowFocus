using FlowFocus.Blazor.Dialogs;
using FlowFocus.Core;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FlowFocus.Blazor.Layout;

public partial class MainLayout
{
    [Inject] public ISettingsRepository SettingsRepo { get; set; } = null!;
    [Inject] public IDialogService DialogService { get; set; } = null!;
    [Inject] public IPlannerService PlannerService { get; set; } = null!;
    [Inject] public INotificationService NotificationService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;

    private bool _drawerOpen = true;
    private bool _isDarkMode = true;
    private MudTheme? _theme;

    private bool _isRecalculating = false;
    private bool _autoDistributeEnabled;

    protected override void OnInitialized()
    {
        base.OnInitialized();

        var settings = SettingsRepo.GetUserSettings();
        _isDarkMode = settings.IsDarkMode;
        _autoDistributeEnabled = settings.AutoDistributeEnabled;

        _theme = new()
        {
            PaletteLight = _lightPalette,
            PaletteDark = _darkPalette,
            LayoutProperties = new()
        };
    }

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }

    private void DarkModeToggle()
    {
        _isDarkMode = !_isDarkMode;
        
        var settings = SettingsRepo.GetUserSettings();
        settings.IsDarkMode = _isDarkMode;
        SettingsRepo.UpdateSettings(settings);
    }

    private async Task RecalculateAllAsync()
    {
        try
        {
            _isRecalculating = true;

            var settings = SettingsRepo.GetUserSettings();
            if (settings == null)
            {
                Snackbar.Add("Настройки не найдены", Severity.Warning);
                return;
            }

            settings.AutoDistributeEnabled = _autoDistributeEnabled;
            SettingsRepo.UpdateSettings(settings);

            await Task.Run(() => PlannerService.RecalculateAll(settings));

            NotificationService.NotifyTasksChanged();
            Snackbar.Add("Все задачи пересчитаны", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка пересчёта: {ex.Message}", Severity.Error);
        }
        finally
        {
            _isRecalculating = false;
            StateHasChanged();
        }
    }

    private async Task OpenProcrastinationDialog()
    {
        DialogOptions options = new()
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseOnEscapeKey = true
        };

        await DialogService.ShowAsync<ProcrastinationDialog>("Прокрастинация", options);
    }

    private void OnAutoDistributeChanged(bool value)
    {
        _autoDistributeEnabled = value;

        try
        {
            var settings = SettingsRepo.GetUserSettings();
            if (settings == null) return;
            settings.AutoDistributeEnabled = value;
            SettingsRepo.UpdateSettings(settings);
        }
        catch
        {
        }
    }

    private readonly PaletteLight _lightPalette = new()
    {
        Black = "#110e2d",
        AppbarText = "#424242",
        AppbarBackground = "rgba(255,255,255,0.95)",
        DrawerBackground = "#ffffff",
        GrayLight = "#e8e8e8",
        GrayLighter = "#f9f9f9",
        Secondary = "#78909C",
        SecondaryDarken = "#546E7A",
        SecondaryLighten = "#90A4AE",
        Primary = "#5c6bc0",
        Surface = "#ffffff",
        Background = "#f5f5f5"
    };

    private readonly PaletteDark _darkPalette = new()
    {
        Primary = "#7e6fff",
        Surface = "#1e1e2d",
        Background = "#1a1a27",
        BackgroundGray = "#151521",
        AppbarText = "#92929f",
        AppbarBackground = "rgba(26,26,39,0.95)",
        DrawerBackground = "#1a1a27",
        ActionDefault = "#74718e",
        ActionDisabled = "#9999994d",
        ActionDisabledBackground = "#605f6d4d",
        TextPrimary = "#b2b0bf",
        TextSecondary = "#92929f",
        TextDisabled = "#ffffff33",
        DrawerIcon = "#92929f",
        DrawerText = "#92929f",
        GrayLight = "#2a2833",
        GrayLighter = "#1e1e2d",
        Info = "#4a86ff",
        Success = "#3dcb6c",
        Warning = "#ffb545",
        Error = "#ff3f5f",
        LinesDefault = "#33323e",
        TableLines = "#33323e",
        Divider = "#292838",
        OverlayLight = "#1e1e2d80",
        Secondary = "#82B1FF",
        SecondaryDarken = "#448AFF",
        SecondaryLighten = "#BBDEFB",
    };

    private string DarkLightModeButtonIcon => _isDarkMode switch
    {
        true => Icons.Material.Rounded.LightMode,
        false => Icons.Material.Outlined.DarkMode,
    };
}
