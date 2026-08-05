using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace FlowFocus.Blazor.Pages;

public partial class Settings
{
    [Inject] public ISettingsRepository SettingsRepo { get; set; } = null!;
    [Inject] public IPriorityRepository PriorityRepo { get; set; } = null!;
    [Inject] public INotificationService NotificationService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;

    private UserSettings _settings = new();
    private List<PriorityItem> _priorityItems = [];
    private int _dailyTimeHours;

    private static readonly string[] ColorPalette =
    [
        "#FF4444", "#FF8C00", "#FFD700", "#4CAF50", "#2196F3",
        "#9C27B0", "#E91E63", "#00BCD4", "#795548", "#607D8B",
        "#FF5722", "#8BC34A", "#03A9F4", "#673AB7", "#F44336",
        "#FFEB3B", "#009688", "#3F51B5", "#CDDC39", "#FF9800"
    ];

    private MudDropContainer<PriorityItem>? _dropContainer;

    protected override void OnInitialized()
    {
        LoadSettings();
    }

    private void LoadSettings()
    {
        _settings = SettingsRepo.GetUserSettings();
        _dailyTimeHours = _settings.DailyTimeLimit / 60;
        
        var priorities = PriorityRepo.GetAllOrdered();
        _priorityItems = priorities.Select(p => new PriorityItem
        {
            Id = p.Id,
            Order = p.Order,
            Name = p.Name,
            Color = p.Color,
        }).ToList();
    }

    private void OnPriorityDropped(MudItemDropInfo<PriorityItem> info)
    {
        if (info.Item == null) return;

        var item = info.Item;
        _priorityItems.Remove(item);
        _priorityItems.Insert(info.IndexInZone, item);

        for (var i = 0; i < _priorityItems.Count; i++)
        {
            _priorityItems[i].Order = i + 1;
        }
    }

    private void SetPriorityColor(PriorityItem item, string color)
    {
        item.Color = color;
    }

    private void AddPriority()
    {
        int newId;
        if (_priorityItems.Any(p => p.Id <= 0))
        {
            newId = _priorityItems.Where(p => p.Id <= 0).Min(p => p.Id) - 1;
        }
        else
        {
            newId = -1;
        }

        var unusedColor = ColorPalette.FirstOrDefault(c => _priorityItems.All(p => p.Color != c)) ?? "#808080";

        _priorityItems.Add(new()
        {
            Id = newId,
            Name = "Новый приоритет",
            Color = unusedColor,
            Order = _priorityItems.Count + 1,
        });
        _dropContainer?.Refresh();
    }

    private void DeletePriority(PriorityItem item)
    {
        if (_priorityItems.Count <= 1)
        {
            Snackbar.Add("Нельзя удалить последний приоритет", Severity.Warning);
            return;
        }

        if (_settings.DefaultPriorityId == item.Id)
        {
            _settings.DefaultPriorityId = null;
        }

        _priorityItems.Remove(item);

        for (var i = 0; i < _priorityItems.Count; i++)
        {
            _priorityItems[i].Order = i + 1;
        }

        if (item.Id > 0)
        {
            PriorityRepo.Delete(item.Id);
        }
        
        _dropContainer?.Refresh();
    }

    private void ResetToDefaults()
    {
        _settings.DayStartHour = 5;
        _settings.DailyTaskLimit = 10;
        _settings.DailyComplexityLimit = 100;
        _dailyTimeHours = 8;
        _settings.AutoDistributeEnabled = false;
        _settings.DefaultPriorityId = null;
        Snackbar.Add("Настройки сброшены к значениям по умолчанию", Severity.Info);
    }

    private void SaveSettings()
    {
        try
        {
            _settings.DailyTimeLimit = _dailyTimeHours * 60;
            SettingsRepo.UpdateSettings(_settings);

            for (var i = 0; i < _priorityItems.Count; i++)
            {
                _priorityItems[i].Order = i + 1;
            }

            foreach (var item in _priorityItems)
            {
                PriorityLevel priority = new()
                {
                    Id = item.Id > 0 ? item.Id : 0,
                    Order = item.Order,
                    Name = item.Name,
                    Color = item.Color,
                };

                if (item.Id > 0)
                {
                    PriorityRepo.Update(priority);
                }
                else
                {
                    PriorityRepo.Add(priority);
                    item.Id = priority.Id;
                }
            }

            PriorityRepo.Reorder(_priorityItems.Where(p => p.Id > 0).Select(p => p.Id).ToList());

            NotificationService.NotifySettingsChanged();
            Snackbar.Add("Настройки сохранены", Severity.Success);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Ошибка сохранения: {ex.Message}", Severity.Error);
        }
    }

    private class PriorityItem
    {
        public int Id { get; set; }
        public int Order { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = "#808080";
    }
}
