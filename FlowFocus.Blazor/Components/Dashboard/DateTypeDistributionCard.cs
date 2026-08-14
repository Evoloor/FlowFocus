namespace FlowFocus.Blazor.Components.Dashboard;

using FlowFocus.Core.Enums;
using MudBlazor;

/// <summary>
/// Карточка распределения задач по источникам дат (Назначение дат).
/// Наследует единый базовый компонент BaseEnumDistributionCard.
/// </summary>
public class DateTypeDistributionCard : BaseEnumDistributionCard<DateSource>
{
    protected override string CardTitle => "Назначение дат";
    protected override string EmptyStateMessage => "Нет распределения по датам";

    protected override IEnumerable<EnumDistributionItemConfig<DateSource>> GetItemConfigs()
    {
        yield return new EnumDistributionItemConfig<DateSource>(
            DateSource.Manual,
            "Manual (Ручная)",
            Icons.Material.Filled.EditCalendar,
            Color.Primary,
            "color: #6366f1;"
        );
        yield return new EnumDistributionItemConfig<DateSource>(
            DateSource.AutoFixed,
            "Autofixed (Повтор)",
            Icons.Material.Filled.Autorenew,
            Color.Info,
            "color: #06b6d4;"
        );
        yield return new EnumDistributionItemConfig<DateSource>(
            DateSource.AutoFlexible,
            "Autoflex (Свободные)",
            Icons.Material.Filled.Grain,
            Color.Secondary,
            "color: #a855f7;"
        );
    }
}
