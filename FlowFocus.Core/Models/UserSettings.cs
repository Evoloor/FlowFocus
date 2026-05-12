using System.ComponentModel.DataAnnotations;

namespace FlowFocus.Core.Models;

/// <summary>
/// Настройки пользователя
/// </summary>
public class UserSettings : IAuditEntity
{
    public int Id { get; set; } = 1;
    public DateTime LastChangesOn { get; set; }

    // === Время ===
    /// <summary>Время начала дня (часы, 0-23)</summary>
    [Range(0, 23)]
    public int DayStartHour { get; set; } = 5;

    // === Лимиты на день ===
    /// <summary>Максимальная суммарная сложность задач на день</summary>
    [Range(1, 1000)]
    public int DailyComplexityLimit { get; set; } = 100;

    /// <summary>Максимальное суммарное время задач на день (минуты)</summary>
    [Range(1, 1440)]
    public int DailyTimeLimit { get; set; } = 480;

    /// <summary>Максимальное количество задач на день</summary>
    [Range(1, 100)]
    public int DailyTaskLimit { get; set; } = 10;

    // === Авто-распределение ===
    /// <summary>Включено ли авто-распределение при добавлении/изменении задач</summary>
    public bool AutoDistributeEnabled { get; set; }

    // === Интерфейс ===
    /// <summary>Тёмная тема</summary>
    public bool IsDarkMode { get; set; } = true;

    /// <summary>Скрывать названия задач под спойлер по умолчанию</summary>
    public bool HideTaskTitlesDefault { get; set; }

    /// <summary>ID приоритета по умолчанию</summary>
    public int? DefaultPriorityId { get; set; }
}