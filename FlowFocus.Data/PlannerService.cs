using FlowFocus.Core;
using FlowFocus.Core.Models;
using FlowFocus.Data.Services;

namespace FlowFocus.Data;

/// <summary>
/// Сервис алгоритмического планирования задач (Фасад / Оркестратор)
/// </summary>
public class PlannerService(ITaskRepository taskRepository) : IPlannerService
{
    private readonly PriorityEscalationPlanner _priorityPlanner = new(taskRepository);
    private readonly TaskDistributionPlanner _distributionPlanner = new(taskRepository);

    public void ActualizePriorities() => _priorityPlanner.ActualizePriorities();

    public void NormalizeBlockerPriorities() => _priorityPlanner.NormalizeBlockerPriorities();

    public void DistributeTasks(UserSettings settings) => _distributionPlanner.DistributeTasks(settings);

    public void RecalculateAll(UserSettings settings)
    {
        taskRepository.NormalizeTaskDateSources(saveChanges: false);
        ActualizePriorities();
        DistributeTasks(settings);
        UpdateBlockedStatuses();

        taskRepository.SaveChangesAndNotify();
    }

    public void UpdateBlockedStatuses() => _priorityPlanner.UpdateBlockedStatuses();
}
