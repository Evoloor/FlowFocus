namespace FlowFocus.Core.Models;

/// <summary>
/// Единая абстрактная базовая модель для частотных распределений по enum-типам.
/// Наследует Dictionary&lt;TEnum, int&gt; для сохранения обратной совместимости.
/// </summary>
public abstract class BaseEnumDistribution<TEnum> : Dictionary<TEnum, int>
    where TEnum : struct, Enum
{
    protected BaseEnumDistribution() { }

    protected BaseEnumDistribution(IDictionary<TEnum, int> dictionary)
        : base(dictionary) { }

    /// <summary>
    /// Общее количество элементов во всех категориях распределения
    /// </summary>
    public int TotalCount => Values.Sum();

    /// <summary>
    /// Получить количество элементов для указанного enum-ключа
    /// </summary>
    public int GetCount(TEnum key) => TryGetValue(key, out var count) ? count : 0;

    /// <summary>
    /// Рассчитать процентную долю элементов для указанного enum-ключа (0.0 .. 100.0)
    /// </summary>
    public double GetPercentage(TEnum key)
    {
        if (TotalCount == 0) return 0.0;
        return Math.Round(((double)GetCount(key) / TotalCount) * 100.0, 1);
    }
}
