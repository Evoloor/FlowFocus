namespace FlowFocus.Core;

/// <summary>
/// Todo-день — логическая дата с учётом настройки времени начала дня.
/// Используется во всей логике вчера/сегодня/завтра: фильтры, просрочка,
/// дата выполнения, эскалации, распределение задач.
/// </summary>
public readonly struct TodoDay(DateTime date) : IComparable<TodoDay>, IEquatable<TodoDay>
{
    private static int _dayStartHour = 5;
    private static TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>
    /// Установить время начала дня. Вызывается при старте приложения
    /// и при изменении настройки пользователем.
    /// </summary>
    public static void Configure(int dayStartHour) => _dayStartHour = dayStartHour;

    /// <summary>
    /// Установить провайдер времени (для тестирования или переопределения)
    /// </summary>
    public static void SetTimeProvider(TimeProvider? timeProvider) => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Получить текущий Todo-день с использованием провайдера времени</summary>
    public static TodoDay GetToday(TimeProvider? timeProvider = null)
    {
        var provider = timeProvider ?? _timeProvider;
        var now = provider.GetLocalNow().DateTime;
        return now.Hour < _dayStartHour
            ? new(now.Date.AddDays(-1))
            : new(now.Date);
    }

    /// <summary>Текущий Todo-день с учётом настройки времени начала дня</summary>
    public static TodoDay Today => GetToday();

    /// <summary>Календарная дата этого Todo-дня (всегда без времени)</summary>
    public DateTime Date { get; } = date.Date;

    // --- Арифметика ---

    public TodoDay AddDays(int days) => new(Date.AddDays(days));
    public TodoDay Tomorrow => AddDays(1);
    public TodoDay Yesterday => AddDays(-1);

    // --- Проверки относительно этого дня ---

    /// <summary>Является ли дата задачи просроченной относительно этого Todo-дня</summary>
    public bool IsOverdue(DateTime? taskDate) =>
        taskDate.HasValue && new TodoDay(taskDate.Value) < this;

    /// <summary>Является ли дата задачи "сегодняшней" в терминах этого Todo-дня</summary>
    public bool IsSameDay(DateTime? taskDate) =>
        taskDate.HasValue && new TodoDay(taskDate.Value) == this;

    /// <summary>Является ли дата задачи "завтрашней" в терминах этого Todo-дня</summary>
    public bool IsTomorrow(DateTime? taskDate) =>
        taskDate.HasValue && new TodoDay(taskDate.Value) == Tomorrow;

    // --- Преобразование ---

    /// <summary>Преобразовать в DateTime (для записи в БД, отображения в UI)</summary>
    public DateTime ToDateTime() => Date;

    // --- Сравнение ---

    public int CompareTo(TodoDay other) => Date.CompareTo(other.Date);
    public bool Equals(TodoDay other) => Date == other.Date;
    public override bool Equals(object? obj) => obj is TodoDay other && Equals(other);
    public override int GetHashCode() => Date.GetHashCode();

    public static bool operator ==(TodoDay a, TodoDay b) => a.Equals(b);
    public static bool operator !=(TodoDay a, TodoDay b) => !a.Equals(b);
    public static bool operator <(TodoDay a, TodoDay b) => a.Date < b.Date;
    public static bool operator >(TodoDay a, TodoDay b) => a.Date > b.Date;
    public static bool operator <=(TodoDay a, TodoDay b) => a.Date <= b.Date;
    public static bool operator >=(TodoDay a, TodoDay b) => a.Date >= b.Date;

    public override string ToString() => Date.ToString("yyyy-MM-dd");
    public string ToString(string format) => Date.ToString(format);
}
