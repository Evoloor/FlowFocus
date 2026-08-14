namespace FlowFocus.Blazor.Components.Dashboard;

using FlowFocus.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;

/// <summary>
/// Единый абстрактный базовый Blazor-компонент карточки частотного распределения по enum-ключам.
/// Отрисовывает унифицированную верстку карточки, прогресс-бары, счетчики и нулевые состояния.
/// </summary>
public abstract class BaseEnumDistributionCard<TEnum> : ComponentBase
    where TEnum : struct, Enum
{
    [Parameter]
    public BaseEnumDistribution<TEnum>? Data { get; set; }

    protected abstract string CardTitle { get; }
    protected abstract string EmptyStateMessage { get; }
    protected abstract IEnumerable<EnumDistributionItemConfig<TEnum>> GetItemConfigs();

    protected int TotalCount => Data?.TotalCount ?? 0;
    protected int GetCount(TEnum key) => Data?.GetCount(key) ?? 0;
    protected double GetPercentage(TEnum key) => Data?.GetPercentage(key) ?? 0.0;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // 0. <MudCard Elevation="3" Class="chart-card glass-card">
        builder.OpenComponent<MudCard>(0);
        builder.AddAttribute(1, "Elevation", 3);
        builder.AddAttribute(2, "Class", "chart-card glass-card");
        builder.AddAttribute(3, "ChildContent", (RenderFragment)(builder2 =>
        {
            // 1. <MudCardHeader>
            builder2.OpenComponent<MudCardHeader>(4);
            builder2.AddAttribute(5, "CardHeaderContent", (RenderFragment)(builder3 =>
            {
                builder3.OpenComponent<MudText>(6);
                builder3.AddAttribute(7, "Typo", Typo.h6);
                builder3.AddAttribute(8, "Class", "chart-title");
                builder3.AddAttribute(9, "ChildContent", (RenderFragment)(builder4 => builder4.AddContent(10, CardTitle)));
                builder3.CloseComponent();
            }));
            builder2.CloseComponent();

            // 2. <MudCardContent>
            builder2.OpenComponent<MudCardContent>(11);
            builder2.AddAttribute(12, "ChildContent", (RenderFragment)(builder3 =>
            {
                if (TotalCount == 0)
                {
                    builder3.OpenComponent<DashboardEmptyState>(13);
                    builder3.AddAttribute(14, "Message", EmptyStateMessage);
                    builder3.CloseComponent();
                }
                else
                {
                    builder3.OpenElement(15, "div");
                    builder3.AddAttribute(16, "class", "date-type-grid");

                    int seq = 17;
                    foreach (var item in GetItemConfigs())
                    {
                        var count = GetCount(item.Key);
                        var percentage = GetPercentage(item.Key);

                        builder3.OpenElement(seq++, "div");
                        builder3.AddAttribute(seq++, "class", "date-type-item");

                        // Header
                        builder3.OpenElement(seq++, "div");
                        builder3.AddAttribute(seq++, "class", "date-type-header");
                        builder3.OpenComponent<MudIcon>(seq++);
                        builder3.AddAttribute(seq++, "Icon", item.Icon);
                        builder3.AddAttribute(seq++, "Size", Size.Small);
                        builder3.AddAttribute(seq++, "Style", item.IconStyle ?? "color: var(--mud-palette-text-secondary);");
                        builder3.CloseComponent();

                        builder3.OpenElement(seq++, "span");
                        builder3.AddContent(seq++, item.Label);
                        builder3.CloseElement();
                        builder3.CloseElement(); // end date-type-header

                        // Body
                        builder3.OpenElement(seq++, "div");
                        builder3.AddAttribute(seq++, "class", "date-type-body");

                        builder3.OpenElement(seq++, "span");
                        builder3.AddAttribute(seq++, "class", "date-type-count");
                        builder3.AddContent(seq++, count.ToString());
                        builder3.CloseElement();

                        builder3.OpenElement(seq++, "span");
                        builder3.AddAttribute(seq++, "class", "date-type-percentage");
                        builder3.AddContent(seq++, $"{percentage}%");
                        builder3.CloseElement();
                        builder3.CloseElement(); // end date-type-body

                        // Progress Linear
                        builder3.OpenComponent<MudProgressLinear>(seq++);
                        builder3.AddAttribute(seq++, "Color", item.ProgressColor);
                        builder3.AddAttribute(seq++, "Value", percentage);
                        builder3.AddAttribute(seq++, "Class", "mt-1 rounded");
                        builder3.AddAttribute(seq++, "Height", "4px");
                        builder3.CloseComponent();

                        builder3.CloseElement(); // end date-type-item
                    }

                    builder3.CloseElement(); // end date-type-grid
                }
            }));
            builder2.CloseComponent();
        }));
        builder.CloseComponent();

        // 3. <style> block for distribution cards
        builder.OpenElement(120, "style");
        builder.AddContent(121, @"
            .chart-card {
                border-radius: 16px;
                background: rgba(30, 34, 45, 0.6);
                backdrop-filter: blur(12px);
                border: 1px solid rgba(255, 255, 255, 0.08);
                height: 100%;
            }

            .chart-title {
                font-weight: 600;
                font-size: 1.05rem;
            }

            .date-type-grid {
                display: flex;
                flex-direction: column;
                gap: 16px;
            }

            .date-type-item {
                background: rgba(255, 255, 255, 0.03);
                padding: 12px 14px;
                border-radius: 12px;
                border: 1px solid rgba(255, 255, 255, 0.05);
            }

            .date-type-header {
                display: flex;
                align-items: center;
                gap: 8px;
                font-size: 0.85rem;
                color: var(--mud-palette-text-secondary);
                margin-bottom: 6px;
            }

            .date-type-body {
                display: flex;
                align-items: baseline;
                justify-content: space-between;
            }

            .date-type-count {
                font-size: 1.25rem;
                font-weight: 700;
            }

            .date-type-percentage {
                font-size: 0.85rem;
                color: var(--mud-palette-text-secondary);
            }
        ");
        builder.CloseElement();
    }
}
