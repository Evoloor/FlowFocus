using FlowFocus.Blazor.EditDialogContents;
using FlowFocus.Blazor.EditDialogContents.Validators;
using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Core.Services;
using FlowFocus.Data;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using SubtaskDto = FlowFocus.Blazor.EditDialogContents.SubtaskDto;

namespace FlowFocus.Blazor.Dialogs;

public partial class TaskEditDialog
{
    [Inject] public ITaskRepository TaskRepo { get; set; } = null!;
    [Inject] public ISettingsRepository SettingsRepo { get; set; } = null!;
    [Inject] public IPriorityRepository PriorityRepo { get; set; } = null!;
    [Inject] public ITagRepository TagRepo { get; set; } = null!;
    [Inject] public ITagSessionService TagSessionService { get; set; } = null!;
    [Inject] public IPlannerService PlannerService { get; set; } = null!;
    [Inject] public INotificationService NotificationService { get; set; } = null!;
    [Inject] public ISnackbar Snackbar { get; set; } = null!;

    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public TaskItem? ExistingTask { get; set; }

    [Parameter] public string? InitialTitle { get; set; }

    [Parameter] public bool IsSubtaskMode { get; set; }

    private bool _isFormValid;
    private TaskItem _task = new();
    private TaskItem? _originalTask; // Для отслеживания изменений
    // Local mirror of the scheduled date; changes via the date picker set DateSource explicitly
    private DateTime? _scheduledDate;
    private List<PriorityLevel> _priorities = [];
    private List<Tag> _suggestedTags = [];
    private List<Tag> _selectedTags = [];
    private HashSet<int> _selectedTagIds = [];
    private List<ExternalCondition> _selectedConditions = [];
    private HashSet<int> _selectedConditionIds = [];
    // Исходные id тегов при открытии диалога — нужны для расчёта удалённых тегов при сохранении
    private HashSet<int> _originalTagIds = [];
    private List<SubtaskDto> _subtasks = [];
    private List<RelationDto> _relations = [];
    private readonly List<EscalationDto> _escalations = [];
    private List<TaskItem> _availableTasks = [];
    private UserSettings? _settings;

    private int? _estimatedValue;
    private TimeFormat _timeFormat = TimeFormat.Minutes;

    private bool IsEdit => ExistingTask != null;

    // Список id связей, помеченных для удаления при сохранении
    private readonly List<int> _relationsToRemove = [];

    // Отслеживаем переход в состояние повторяющейся задачи
    private bool _wasRecurring;

    protected override async Task OnInitializedAsync()
    {
        _settings = SettingsRepo.GetUserSettings();
        _priorities = PriorityRepo.GetAllOrdered();
        _availableTasks = TaskRepo.GetTasksForAutocomplete();

        if (ExistingTask != null)
        {
            _task = new(ExistingTask);
            _originalTask = new(ExistingTask); // Сохраняем оригинал для сравнения
            LoadExistingTaskData();

            // If editing existing task, and it has no user priority set, apply default from settings
            _task.PriorityId ??= _settings?.DefaultPriorityId;
        }
        else
        {
            _task = new()
            {
                Title = InitialTitle ?? string.Empty,
                Status = TaskStatus.Planned,
                CreatedDate = DateTime.UtcNow,
                PriorityId = _settings?.DefaultPriorityId
            };
            _originalTask = null;
            StateHasChanged();
        }

        // Синхронизируем локальное зеркало даты из модели
        _scheduledDate = _task.ScheduledDate;

        // Конвертируем время
        if (_task.EstimatedMinutes != null)
        {
            if (_task.EstimatedMinutes >= 60 && _task.EstimatedMinutes % 60 == 0)
            {
                _estimatedValue = _task.EstimatedMinutes / 60;
                _timeFormat = TimeFormat.Hours;
            }
            else
            {
                _estimatedValue = _task.EstimatedMinutes;
                _timeFormat = TimeFormat.Minutes;
            }
        }

        // Инициализируем флаг отслеживания повторяемости
        _wasRecurring = _task.IsRecurring;

        // Обновим предлагаемые теги с учётом уже выбранных
        await RefreshSuggestedTags();
    }

    private void LoadExistingTaskData()
    {   
        var source = ExistingTask;
        if (ExistingTask is { Id: > 0 })
        {
            var repoVersion = TaskRepo.GetById(ExistingTask.Id);
            if (repoVersion != null) source = repoVersion;
        }

        if (source != null && source.Tags.Any())
        {
            _selectedTags = source.Tags.Select(tt => tt.Tag).Where(t => t != null).ToList();
            _selectedTagIds = _selectedTags.Select(t => t.Id).ToHashSet();
            _originalTagIds = new(_selectedTagIds);
        }
        else
        {
            _selectedTags = [];
            _selectedTagIds = [];
            _originalTagIds = [];
        }

        if (source != null && source.Conditions.Any())
        {
            _selectedConditions = source.Conditions.Select(tc => tc.Condition).Where(c => c != null).ToList();
            _selectedConditionIds = _selectedConditions.Select(c => c.Id).ToHashSet();
        }
        else
        {
            _selectedConditions = [];
            _selectedConditionIds = [];
        }

        _subtasks = (source?.Subtasks ?? []).Select(s => new SubtaskDto
        {
            Id = s.Id,
            Title = s.Title,
            HideUnderSpoiler = s.HideUnderSpoiler,
            IsFavorite = s.IsFavorite,
            Interest = s.Interest,
            Complexity = s.Complexity,
            EstimatedMinutes = s.EstimatedMinutes
        }).ToList();

        var outgoing = (source?.Relations ?? []).Select(r => new RelationDto
        {
            Id = r.Id,
            Type = r.Type,
            TargetTask = r.TargetTask
        });

        var incoming = (source?.InverseRelations ?? []).Select(r => new RelationDto
        {
            Id = r.Id,
            Type = r.Type == RelationType.Blocks ? RelationType.BlockedBy : r.Type,
            TargetTask = r.SourceTask
        });

        _relations = outgoing.Concat(incoming)
            .Where(r => r.TargetTask != null)
            .GroupBy(r => new { r.TargetTask!.Id, r.Type })
            .Select(g => g.First())
            .ToList();

        _escalations.Clear();
        if (source?.PriorityEscalations == null) return;
        foreach (var pe in source.PriorityEscalations)
        {
            _escalations.Add(new()
            {
                Id = pe.Id,
                TargetPriorityId = pe.TargetPriorityId,
                EscalationDate = pe.EscalationDate
            });
        }
    }

    private bool ShouldShowEscalationTable
    {
        get
        {
            var highestPriority = _priorities.FirstOrDefault();
            if (highestPriority == null) return false;

            return _task.PriorityId == null || _task.PriorityId != highestPriority.Id;
        }
    }

    private async Task AddTag(Tag tag)
    {
        if (_selectedTagIds.Contains(tag.Id)) return;

        _selectedTags.Add(tag);
        _selectedTagIds.Add(tag.Id);
        TagSessionService.MarkTagUsed(tag);

        await RefreshSuggestedTags();
    }

    private void RemoveTag(Tag tag)
    {
        if (tag == null) return;
        _selectedTags.RemoveAll(t => t.Id == tag.Id);
        _selectedTagIds.Remove(tag.Id);
    }

    private async Task OnSubtasksChanged(List<SubtaskDto> newSubtasks)
    {
        _subtasks = newSubtasks ?? [];
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRelationRemoveRequested(RelationDto? relation)
    {
        if (relation == null)
            return;

        if (relation.Id is > 0)
        {
            await RemoveRelationById(relation.Id);
        }
        else
        {
            _relations.Remove(relation);
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RemoveRelationById(int? relationId)
    {
        if (relationId is null || relationId <= 0)
        {
            return;
        }

        if (!_relationsToRemove.Contains(relationId.Value)) _relationsToRemove.Add(relationId.Value);
        _relations.RemoveAll(r => r.Id == relationId);

        try
        {
            TaskRepo.DeleteRelation(relationId.Value);
            PlannerService.UpdateBlockedStatuses();
            NotificationService.NotifyTasksChanged();
        }
        catch
        {
        }

        await InvokeAsync(StateHasChanged);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (_wasRecurring != _task.IsRecurring)
        {
            _wasRecurring = _task.IsRecurring;
            if (_wasRecurring)
            {
                foreach (var r in _relations.Where(r => r.Id is > 0).ToList().Where(r => !_relationsToRemove.Contains(r.Id.Value)))
                {
                    _relationsToRemove.Add(r.Id.Value);
                }
                _relations.Clear();

                InvokeAsync(StateHasChanged);
            }
        }

        base.OnAfterRender(firstRender);
    }

    private async Task SaveTask()
    {
        try
        {
            var validationErrors = ValidateTask();
            if (validationErrors.Any())
            {
                foreach (var error in validationErrors)
                {
                    Snackbar.Add(error, Severity.Warning);
                }

                return;
            }

            _task.EstimatedMinutes = _timeFormat switch
            {
                TimeFormat.Hours => _estimatedValue * 60,
                _ => _estimatedValue
            };

            if (_task.IsRecurring && _task.ScheduledDate == null)
            {
                _task.ScheduledDate = TodoDay.Today.ToDateTime();
                _task.DateSource = DateSource.Manual;
                _scheduledDate = _task.ScheduledDate;
            }
            else if (_task.ScheduledDate == null)
            {
                _task.DateSource = DateSource.AutoFlexible;
            }

            if (_task.Status == TaskStatus.NotConfigured && !IsSubtaskMode)
            {
                _task.Status = TaskStatus.Planned;
            }

            _task.Tags = _selectedTags.Select(tag => new TaskTag
            {
                TagId = tag.Id,
            }).ToList();

            _task.Conditions = _selectedConditions.Select(cond => new TaskCondition
            {
                ConditionId = cond.Id,
            }).ToList();

            var existingTracked = ExistingTask is { Id: > 0 } ? TaskRepo.GetById(ExistingTask.Id) : null;

            _task.Subtasks = _subtasks
                .Where(s => !s.IsDeleted)
                .Select(dto =>
                {
                    TaskItem ti = new();
                    if (dto.Id is > 0)
                    {
                        ti.Id = dto.Id.Value;
                        var trackedSub = existingTracked?.Subtasks?.FirstOrDefault(st => st.Id == dto.Id.Value);
                        if (trackedSub != null)
                        {
                            ti.CreatedDate = trackedSub.CreatedDate;
                            ti.ParentTaskId = trackedSub.ParentTaskId;
                            ti.Status = trackedSub.Status;
                        }
                    }

                    ti.Title = dto.Title;
                    ti.HideUnderSpoiler = dto.HideUnderSpoiler;
                    ti.IsFavorite = dto.IsFavorite;
                    ti.Interest = dto.Interest;
                    ti.Complexity = dto.Complexity;
                    ti.EstimatedMinutes = dto.EstimatedMinutes;
                    ti.Status = ti.Status == 0 ? TaskStatus.Planned : ti.Status;

                    return ti;
                }).ToList();

            if (_task.IsRecurring)
            {
                if (ExistingTask is { Id: > 0 })
                {
                    var existing = TaskRepo.GetById(ExistingTask.Id);
                    if (existing?.Relations != null)
                    {
                        foreach (var rel in existing.Relations.Where(rel => !_relationsToRemove.Contains(rel.Id)))
                        {
                            _relationsToRemove.Add(rel.Id);
                        }
                    }
                }
                _relations.Clear();
            }

            var relationsToRecurringTargets = _relations.Where(r => r.TargetTask is { IsRecurring: true }).ToList();
            if (relationsToRecurringTargets.Any())
            {
                var titles = relationsToRecurringTargets.Select(r => r.TargetTask!.Title).Where(t => !string.IsNullOrWhiteSpace(t)).Take(5).ToList();
                var message = titles.Any() ? $"Связи с повторяющимися задачами удалены: {string.Join(", ", titles)}" : "Некоторые связи с повторяющимися задачами удалены";
                Snackbar.Add(message, Severity.Warning);
                _relations.RemoveAll(r => r.TargetTask is { IsRecurring: true });
            }

            _task.Relations = RelationModule.SyncRelationsToTask(_relations, _task, existingTracked);

            if (_relationsToRemove.Count > 0)
            {
                foreach (var relId in _relationsToRemove.ToList())
                {
                    try
                    {
                        TaskRepo.DeleteRelation(relId);
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Add($"Не удалось удалить связь (id={relId}): {ex.Message}", Severity.Error);
                    }
                }
                _relationsToRemove.Clear();
            }

            _task.PriorityEscalations = EscalationModule.SyncEscalationsToTask(_escalations, _task, ExistingTask, _priorities);

            if (IsEdit)
            {
                TaskRepo.Update(_task);
                Snackbar.Add("Задача обновлена", Severity.Success);
            }
            else
            {
                TaskRepo.Add(_task);
                Snackbar.Add("Задача создана", Severity.Success);
            }

            if (_settings?.AutoDistributeEnabled == true && ShouldRecalculate())
            {
                await InvokeAsync(() =>
                {
                    PlannerService.RecalculateAll(_settings);
                    return Task.CompletedTask;
                });
            }
            else if (RelationsChanged())
            {
                await InvokeAsync(() =>
                {
                    PlannerService.UpdateBlockedStatuses();
                    return Task.CompletedTask;
                });
            }

            await InvokeAsync(() =>
            {
                NotificationService.NotifyTasksChanged();
                MudDialog.Close(DialogResult.Ok(_task));
                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private void Cancel()
    {
        MudDialog.Cancel();
    }

    private void OnScheduledDateChanged(DateTime? value)
    {
        _scheduledDate = value;
        _task.ScheduledDate = value;
        _task.DateSource = value.HasValue ? DateSource.Manual : DateSource.AutoFlexible;
    }

    private List<string> ValidateTask()
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(_task.Title)) errors.Add("Название обязательно");

        if (_task.IsRecurring && _task.ScheduledDate == null)
        {
            _task.ScheduledDate = TodoDay.Today.ToDateTime();
            _task.DateSource = DateSource.Manual;
            _scheduledDate = _task.ScheduledDate;
        }

        var escalationValidation = TaskEditValidator.ValidateEscalations(_escalations, _task, _priorities);
        if (!escalationValidation.IsValid) errors.AddRange(escalationValidation.Errors);

        var relationValidation = TaskEditValidator.ValidateRelations(_relations, _task, _priorities);
        if (!relationValidation.IsValid) errors.AddRange(relationValidation.Errors);

        var subtaskValidation = TaskEditValidator.ValidateSubtasks(_task, _subtasks, _priorities, TaskRepo);
        if (!subtaskValidation.IsValid) errors.AddRange(subtaskValidation.Errors);

        return errors;
    }

    private bool ShouldRecalculate()
    {
        try
        {
            if (_originalTask == null) return true;
            if (_originalTask.PriorityId != _task.PriorityId) return true;
            if (_originalTask.ScheduledDate != _task.ScheduledDate) return true;
        }
        catch
        {
            return true;
        }
        return false;
    }

    private bool RelationsChanged()
    {
        try
        {
            if (_relationsToRemove.Count > 0) return true;
            var originalCount = _originalTask?.Relations?.Count ?? 0;
            if (_relations.Count != originalCount) return true;
        }
        catch
        {
            return true;
        }
        return false;
    }

    private async Task RefreshSuggestedTags()
    {
        var allSuggested = TagSessionService.GetSuggestedTags(10);
        _suggestedTags = allSuggested.Where(t => !_selectedTagIds.Contains(t.Id)).Take(5).ToList();

        if (_suggestedTags.Count < 5)
        {
            var popular = TagRepo.GetPopularTags(10);
            foreach (var tag in popular.Where(tag => !_selectedTagIds.Contains(tag.Id) && _suggestedTags.All(t => t.Id != tag.Id)))
            {
                if (_suggestedTags.Count >= 5) break;
                _suggestedTags.Add(tag);
            }
        }

        await InvokeAsync(StateHasChanged);
    }
}
