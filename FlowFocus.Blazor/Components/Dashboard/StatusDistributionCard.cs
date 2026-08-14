namespace FlowFocus.Blazor.Components.Dashboard;

using FlowFocus.Core.Enums;
using TaskStatus = FlowFocus.Core.Enums.TaskStatus;
using MudBlazor;

/// <summary>
/// Карточка распределения задач по статусам.
/// Наследует единый базовый компонент BaseEnumDistributionCard.
/// </summary>
public class StatusDistributionCard : BaseEnumDistributionCard<TaskStatus>
{
    protected override string CardTitle => "Распределение по статусам";
    protected override string EmptyStateMessage => "Нет распределения по статусам";

    protected override IEnumerable<EnumDistributionItemConfig<TaskStatus>> GetItemConfigs()
    {
        yield return new EnumDistributionItemConfig<TaskStatus>(
            TaskStatus.NotConfigured,
            "Ненастроена",
            Icons.Material.Filled.Build,
            Color.Warning,
            "color: #f59e0b;"
        );
        yield return new EnumDistributionItemConfig<TaskStatus>(
            TaskStatus.Planned,
            "Запланирована",
            Icons.Material.Filled.Event,
            Color.Primary,
            "color: #3b82f6;"
        );
        yield return new EnumDistributionItemConfig<TaskStatus>(
            TaskStatus.Completed,
            "Завершена",
            Icons.Material.Filled.CheckCircle,
            Color.Success,
            "color: #10b981;"
        );
        yield return new EnumDistributionItemConfig<TaskStatus>(
            TaskStatus.Irrelevant,
            "Неактуальна",
            Icons.Material.Filled.RemoveCircleOutline,
            Color.Secondary,
            "color: #94a3b8;"
        );
        yield return new EnumDistributionItemConfig<TaskStatus>(
            TaskStatus.Blocked,
            "Заблокирована",
            Icons.Material.Filled.Lock,
            Color.Error,
            "color: #ef4444;"
        );
    }
}
