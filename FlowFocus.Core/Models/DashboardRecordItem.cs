namespace FlowFocus.Core.Models;

/// <summary>
/// Элемент рекорда для дашборда
/// </summary>
public class DashboardRecordItem
{
    /// <summary>Название рекорда</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Значение рекорда</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Дата рекорда (если применимо)</summary>
    public DateTime? Date { get; set; }
}
