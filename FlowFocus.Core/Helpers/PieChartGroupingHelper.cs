namespace FlowFocus.Core.Helpers;

/// <summary>
/// DRY хелпер для группировки мелких категорий на круговых диаграммах по канонам и стандартам индустрии (Top N + Porog %).
/// </summary>
public static class PieChartGroupingHelper
{
    /// <summary>
    /// Группирует мелкие или избыточные категории в секцию "Другое" согласно лучшим практикам визуализации данных:
    /// 1. Ограничение Top N (по умолчанию максимум 10 секторов на диаграмме).
    /// 2. Порог малого сектора (меньше thresholdPercent от суммы, по умолчанию 2.5%).
    /// 3. Защита: "Другое" создается только если объединяются минимум 2 категории (чтобы не терять смысл подписи).
    /// 4. Сектор "Другое" всегда располагается в самом конце.
    /// </summary>
    public static Dictionary<string, int> GroupSmallSlices(
        Dictionary<string, int> source,
        string otherLabel = "Другое",
        int maxSlices = 10,
        double thresholdPercent = 0.025)
    {
        if (source == null || source.Count == 0) return new();

        var sortedNonZero = source
            .Where(kv => kv.Value > 0)
            .OrderByDescending(kv => kv.Value)
            .ToList();

        if (sortedNonZero.Count == 0) return new();

        // Если секторов 2 или меньше, группировка не имеет смысла
        if (sortedNonZero.Count <= 2)
        {
            return sortedNonZero.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        var totalSum = sortedNonZero.Sum(kv => kv.Value);
        if (totalSum == 0) return sortedNonZero.ToDictionary(kv => kv.Key, kv => kv.Value);

        var thresholdValue = totalSum * thresholdPercent;

        // Если общее число категорий превышает maxSlices, сохраняем (maxSlices - 1) категорий + 1 место под "Другое"
        var maxKeep = sortedNonZero.Count > maxSlices ? maxSlices - 1 : sortedNonZero.Count;

        // Находим индекс первой категории, значение которой строго меньше порога thresholdValue
        var thresholdIndex = sortedNonZero.FindIndex(kv => kv.Value < thresholdValue);

        // Определяем точку разделения основных секторов и сектора "Другое"
        var splitIndex = thresholdIndex != -1 
            ? Math.Min(maxKeep, thresholdIndex) 
            : maxKeep;

        var itemsToGroup = sortedNonZero.Count - splitIndex;

        // Каноническое правило: "Другое" имеет смысл, только если сворачиваем 2 и более элементов
        if (itemsToGroup < 2)
        {
            return sortedNonZero.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        Dictionary<string, int> result = new();

        // Добавляем основные категории
        for (var i = 0; i < splitIndex; i++)
        {
            result[sortedNonZero[i].Key] = sortedNonZero[i].Value;
        }

        // Суммируем категории для "Другое"
        var otherSum = 0;
        for (var i = splitIndex; i < sortedNonZero.Count; i++)
        {
            otherSum += sortedNonZero[i].Value;
        }

        if (otherSum > 0)
        {
            result[otherLabel] = otherSum;
        }

        return result;
    }
}

