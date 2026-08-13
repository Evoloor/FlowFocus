namespace FlowFocus.Core.Helpers;

/// <summary>
/// DRY хелпер для расчета частотных распределений элементов в сервисах аналитики
/// </summary>
public static class DistributionHelper
{
    /// <summary>
    /// Расчет частотного распределения по ключевому свойству элементов
    /// </summary>
    public static Dictionary<TKey, int> CalculateDistribution<T, TKey>(
        IEnumerable<T> items,
        Func<T, TKey> keySelector)
        where TKey : notnull
    {
        if (items == null) return new Dictionary<TKey, int>();

        return items
            .GroupBy(keySelector)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Расчет частотного распределения по элементам коллекций, вложенных в объекты
    /// </summary>
    public static Dictionary<TKey, int> CalculateCollectionDistribution<T, TKey>(
        IEnumerable<T> items,
        Func<T, IEnumerable<TKey>?> collectionSelector)
        where TKey : notnull
    {
        if (items == null) return new Dictionary<TKey, int>();

        return items
            .SelectMany(t => collectionSelector(t) ?? Array.Empty<TKey>())
            .GroupBy(k => k)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
