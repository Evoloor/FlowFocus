namespace FlowFocus.Blazor.Components.Dashboard;

using MudBlazor;

/// <summary>
/// Конфигурация для визуального отображения отдельного элемента enum-распределения в карточке
/// </summary>
public record EnumDistributionItemConfig<TEnum>(
    TEnum Key,
    string Label,
    string Icon,
    Color ProgressColor = Color.Primary,
    string? IconStyle = null
) where TEnum : struct, Enum;
