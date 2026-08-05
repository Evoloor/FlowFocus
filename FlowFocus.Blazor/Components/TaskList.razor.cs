using FlowFocus.Blazor.Helpers;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;

namespace FlowFocus.Blazor.Components;

public partial class TaskList : IDisposable
{
    [Inject] public ITaskRepository TaskRepo { get; set; } = null!;
    [Inject] public ISettingsRepository SettingsRepo { get; set; } = null!;
    [Inject] public INotificationService NotificationService { get; set; } = null!;

    // === Параметры отображения ===
    [Parameter] public string? Title { get; set; }
    [Parameter] public bool ShowTitle { get; set; } = true;
    [Parameter] public bool ShowSearch { get; set; } = true;
    [Parameter] public bool ShowSortDropdown { get; set; } = true;
    [Parameter] public bool ShowFilters { get; set; } = true;
    [Parameter] public bool ShowDisplayMode { get; set; } = true;
    [Parameter] public bool ShowPagination { get; set; } = true;
    [Parameter] public bool ShowTags { get; set; } = true;

    // === Фильтры по умолчанию ===
    [Parameter] public TaskListFilter? DefaultFilter { get; set; }
    [Parameter] public Func<List<TaskItem>>? CustomTaskSource { get; set; }
    [Parameter] public EventCallback OnTaskChanged { get; set; }

    // === Внутреннее состояние ===
    private List<TaskItem> _allTasks = [];
    private List<Tag> _availableTags = [];
    private UserSettings? _settings;

    private string _searchQuery = string.Empty;
    private SortType _sortType = SortType.Relevance;
    private DisplayMode _displayMode = DisplayMode.Grid;
    private DateRange? _dateRange;
    private IEnumerable<TaskStatus?> _selectedStatuses = new List<TaskStatus?> { TaskStatus.Planned, TaskStatus.NotConfigured, TaskStatus.Blocked };
    private DurationFilter _durationFilter = DurationFilter.All;
    private IEnumerable<int?> _selectedTagIds = new List<int?>();
    private bool _hideWithDates = false;

    private int _currentPage = 1;
    private int _pageSize = 25;

    private bool HideWithDates
    {
        get => _hideWithDates;
        set
        {
            if (_hideWithDates == value) return;
            _hideWithDates = value;
            _currentPage = 1;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    protected override void OnInitialized()
    {
        _settings = SettingsRepo.GetUserSettings();
        RefreshTasks();
        NotificationService.OnTasksChanged += OnTasksChanged;
    }

    protected override void OnParametersSet()
    {
        RefreshTasks();
    }

    private void OnTasksChanged()
    {
        _ = InvokeAsync(() =>
        {
            RefreshTasks();
            StateHasChanged();
        });
    }

    private void RefreshTasks()
    {
        if (CustomTaskSource != null)
        {
            _allTasks = CustomTaskSource();
        }
        else
        {
            _allTasks = TaskRepo.GetAll()
                .Where(t => t.ParentTaskId == null)
                .ToList();
        }

        _availableTags = _allTasks
            .SelectMany(t => t.Tags.Select(tt => tt.Tag))
            .DistinctBy(t => t.Id)
            .OrderByDescending(t => t.UsageCount)
            .ToList();

        _currentPage = 1;
    }

    private async Task HandleTaskChanged()
    {
        RefreshTasks();
        await OnTaskChanged.InvokeAsync();
    }

    private bool HasActiveFilters =>
        !string.IsNullOrEmpty(_searchQuery) ||
        _dateRange != null ||
        _selectedStatuses.Any() ||
        _durationFilter != DurationFilter.All ||
        _selectedTagIds.Any() ||
        _hideWithDates;

    private List<TaskItem> FilteredTasks
    {
        get
        {
            var tasks = _allTasks.AsEnumerable();
            tasks = TaskFilterEvaluator.ApplyDefaultFilter(tasks, DefaultFilter);
            tasks = TaskFilterEvaluator.ApplySearchAndFilters(tasks, _searchQuery, _dateRange, _selectedStatuses, _durationFilter, _selectedTagIds, _hideWithDates);
            tasks = TaskFilterEvaluator.ApplySort(tasks, _sortType);

            return tasks.ToList();
        }
    }

    private List<TaskItem> PagedTasks
    {
        get
        {
            if (!ShowPagination) return FilteredTasks;

            return FilteredTasks
                .Skip((_currentPage - 1) * _pageSize)
                .Take(_pageSize)
                .ToList();
        }
    }

    private int TotalPages => (int)Math.Ceiling((double)FilteredTasks.Count / _pageSize);

    private void OnPageChanged(int page)
    {
        _currentPage = page;
    }

    private string GetContentClass()
    {
        return _displayMode == DisplayMode.Grid ? "task-list-content-grid" : string.Empty;
    }

    public void Dispose()
    {
        NotificationService.OnTasksChanged -= OnTasksChanged;
    }
}
