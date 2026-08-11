namespace FlowFocus.Core.Helpers;

/// <summary>
/// DRY хелпер для группировки мелких категорий на круговых диаграммах
/// </summary>
public static class PieChartGroupingHelper
{
    /// <summary>
    /// Группирует мелкие категории (<10% по возрастанию, но не более 15% суммарного размера) в категорию "Другое"
    /// </summary>
    public static Dictionary<string, int> GroupSmallSlices(Dictionary<string, int> source, string otherLabel = "Другое")
    {
        if (source == null || source.Count == 0) return new Dictionary<string, int>();

        var nonZero = source.Where(kv => kv.Value > 0).ToList();
        if (nonZero.Count == 0) return new Dictionary<string, int>();

        int totalSum = nonZero.Sum(kv => kv.Value);
        if (totalSum == 0) return new Dictionary<string, int>();

        double tenPercent = totalSum * 0.10;
        double maxOtherLimit = totalSum * 0.15;

        var candidates = nonZero
            .Where(kv => kv.Value < tenPercent)
            .OrderBy(kv => kv.Value)
            .ToList();

        var groupedKeys = new HashSet<string>();
        int otherSum = 0;

        foreach (var candidate in candidates)
        {
            if (otherSum + candidate.Value <= maxOtherLimit)
            {
                otherSum += candidate.Value;
                groupedKeys.Add(candidate.Key);
            }
            else
            {
                break;
            }
        }

        if (groupedKeys.Count < 2)
        {
            return nonZero
                .OrderByDescending(kv => kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        var result = new Dictionary<string, int>();

        foreach (var kv in nonZero.Where(kv => !groupedKeys.Contains(kv.Key)))
        {
            result[kv.Key] = kv.Value;
        }

        if (otherSum > 0)
        {
            result[otherLabel] = otherSum;
        }

        return result.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
