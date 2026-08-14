using FlowFocus.Core.Models;

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
        if (items == null) return new();

        return items
            .GroupBy(keySelector)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>
    /// Расчет частотного распределения по enum-ключу с возвратом строгой модели BaseEnumDistribution
    /// </summary>
    public static TDistribution CalculateEnumDistribution<T, TEnum, TDistribution>(
        IEnumerable<T> items,
        Func<T, TEnum> keySelector)
        where TEnum : struct, Enum
        where TDistribution : BaseEnumDistribution<TEnum>, new()
    {
        var dict = CalculateDistribution(items, keySelector);
        var distribution = new TDistribution();
        foreach (var (key, count) in dict)
        {
            distribution[key] = count;
        }
        return distribution;
    }

    /// <summary>
    /// Расчет частотного распределения по элементам коллекций, вложенных в объекты
    /// </summary>
    public static Dictionary<TKey, int> CalculateCollectionDistribution<T, TKey>(
        IEnumerable<T> items,
        Func<T, IEnumerable<TKey>?> collectionSelector)
        where TKey : notnull
    {
        if (items == null) return new();

        return items
            .SelectMany(t => collectionSelector(t) ?? [])
            .GroupBy(k => k)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}
