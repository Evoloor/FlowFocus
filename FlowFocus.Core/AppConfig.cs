namespace FlowFocus.Core;

/// <summary>
/// Константы конфигурации (настраиваемые на уровне разработки)
/// </summary>
public static class AppConfig
{
    /// <summary>Максимальное количество отображаемых тегов на карточке</summary>
    public const int MaxDisplayedTags = 5;

    /// <summary>Максимальное количество связей с задачами</summary>
    public const int MaxTaskRelations = 15;

    /// <summary>Порог для "коротких" задач (минуты)</summary>
    public const int ShortTaskThreshold = 10;

    /// <summary>Порог для "средних" задач (минуты)</summary>
    public const int MediumTaskThreshold = 60;

    /// <summary>Порог для "долгих" задач (минуты)</summary>
    public const int LongTaskThreshold = 720; // 12 часов

    /// <summary>Порог для "крупных" дел (процент от лимита)</summary>
    public const double LargeTaskThresholdPercent = 0.7;

    /// <summary>Минимальный интерес для прокрастинации</summary>
    public const int MinProcrastinationInterest = 7;
}