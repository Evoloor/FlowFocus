using FlowFocus.Core;
using FlowFocus.Core.Enums;
using FlowFocus.Core.Models;
using FlowFocus.Data.Services;

namespace FlowFocus.Data;

/// <summary>
/// Сервис алгоритмического планирования задач (Фасад / Оркестратор)
/// </summary>
public class PlannerService(
    ITaskRepository taskRepository,
    ISettingsRepository? settingsRepository = null) : IPlannerService
{
    private readonly PriorityEscalationPlanner _priorityPlanner = new(taskRepository);
    private readonly TaskDistributionPlanner _distributionPlanner = new(taskRepository);

    public void ActualizePriorities() => _priorityPlanner.ActualizePriorities();

    public void NormalizeBlockerPriorities() => _priorityPlanner.NormalizeBlockerPriorities();

    public void DistributeTasks(UserSettings settings) => _distributionPlanner.DistributeTasks(settings);

    public void RecalculateAll(UserSettings settings)
    {
        if (settings.TodayPlanLockedDate.HasValue)
        {
            settings.TodayPlanLockedDate = null;
            settingsRepository?.UpdateSettings(settings);
        }

        taskRepository.NormalizeTaskRelations(saveChanges: false);
        taskRepository.NormalizeTaskDateSources(saveChanges: false);
        ActualizePriorities();
        DistributeTasks(settings);
        UpdateBlockedStatuses();

        taskRepository.SaveChangesAndNotify();
    }

    public void UpdateBlockedStatuses() => _priorityPlanner.UpdateBlockedStatuses();

    public void PostponeTask(int taskId, PostponeTarget target, bool replaceWithBacklog, UserSettings? settings = null)
    {
        var effectiveSettings = settings ?? settingsRepository?.GetUserSettings() ?? new UserSettings();
        var task = taskRepository.GetById(taskId);
        if (task == null) return;

        var today = TodoDay.Today;
        var tomorrow = today.Tomorrow;
        var freedMinutes = task.TotalEstimatedMinutes;
        var freedComplexity = task.TotalComplexity;

        var wasTodayLocked = effectiveSettings.TodayPlanLockedDate.HasValue &&
                             new TodoDay(effectiveSettings.TodayPlanLockedDate.Value) == today;

        if (target == PostponeTarget.StrictTomorrow)
        {
            task.ScheduledDate = tomorrow.ToDateTime();
            task.DateSource = DateSource.Manual;
            task.SnoozedUntilDate = tomorrow.ToDateTime();
            taskRepository.Update(task);
        }
        else // PostponeTarget.Auto
        {
            if (task.IsRecurring)
            {
                task.SnoozedUntilDate = tomorrow.ToDateTime();
                taskRepository.Update(task);
                taskRepository.NormalizeTaskDateSources(saveChanges: false);
            }
            else
            {
                task.ScheduledDate = null;
                task.DateSource = DateSource.AutoFlexible;
                task.SnoozedUntilDate = tomorrow.ToDateTime();
                taskRepository.Update(task);
            }
        }

        if (!replaceWithBacklog)
        {
            if (!wasTodayLocked)
            {
                effectiveSettings.TodayPlanLockedDate = today.ToDateTime();
                settingsRepository?.UpdateSettings(effectiveSettings);
            }

            _distributionPlanner.DistributeTasks(effectiveSettings);
        }
        else
        {
            if (wasTodayLocked)
            {
                _distributionPlanner.DistributeTasks(
                    effectiveSettings,
                    allowedReplacementMinutes: freedMinutes,
                    allowedReplacementComplexity: freedComplexity);
            }
            else
            {
                _distributionPlanner.DistributeTasks(effectiveSettings);
            }
        }

        UpdateBlockedStatuses();
        taskRepository.SaveChangesAndNotify();
    }

    public void SetTodayPlanLocked(bool isLocked, UserSettings? settings = null)
    {
        var effectiveSettings = settings ?? settingsRepository?.GetUserSettings() ?? new UserSettings();
        var today = TodoDay.Today;

        effectiveSettings.TodayPlanLockedDate = isLocked ? today.ToDateTime() : null;
        settingsRepository?.UpdateSettings(effectiveSettings);

        if (!isLocked)
        {
            DistributeTasks(effectiveSettings);
            taskRepository.SaveChangesAndNotify();
        }
    }
}
