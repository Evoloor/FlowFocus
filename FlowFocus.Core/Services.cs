using FlowFocus.Core.Models;

namespace FlowFocus.Core;

public interface IRepository<T> where T : class
{
    List<T> GetAll();
    T? GetById(int id);
    void Add(T entity);
    void Update(T entity);
    void Delete(int id);
    void SaveChanges();
}

/// <summary>
/// Репозиторий задач с расширенными методами фильтрации
/// </summary>
public interface ITaskRepository : IRepository<TaskItem>
{
    /// <summary>Получить задачи на указанную дату</summary>
    List<TaskItem> GetTasksForDate(DateTime date);

    /// <summary>Получить задачи на сегодня</summary>
    List<TaskItem> GetTodayTasks(int dayStartHour);

    /// <summary>Получить задачи на завтра</summary>
    List<TaskItem> GetTomorrowTasks();

    /// <summary>Получить ненастроенные задачи</summary>
    List<TaskItem> GetNotConfiguredTasks();

    /// <summary>Получить количество ненастроенных задач</summary>
    int GetNotConfiguredCount();

    /// <summary>Получить просроченные задачи</summary>
    List<TaskItem> GetOverdueTasks(int dayStartHour);

    /// <summary>Получить задачи, которые разблокируются при выполнении указанной задачи</summary>
    List<TaskItem> GetTasksUnblockedBy(int taskId);

    /// <summary>Получить все задачи для автодополнения (без подзадач)</summary>
    List<TaskItem> GetTasksForAutocomplete();

    /// <summary>Пометить задачу как выполненную</summary>
    void CompleteTask(int taskId);

    /// <summary>Пометить задачу как неактуальную</summary>
    void MarkIrrelevant(int taskId);

    /// <summary>Получить интересную задачу для прокрастинации</summary>
    TaskItem? GetProcrastinationTask(List<int> excludeIds);

    /// <summary>Получить наименее приоритетную задачу дня</summary>
    TaskItem? GetLeastPriorityTaskOfDay();
}

/// <summary>
/// Репозиторий настроек пользователя
/// </summary>
public interface ISettingsRepository : IRepository<UserSettings>
{
    UserSettings GetUserSettings();
    void UpdateSettings(UserSettings settings);
}

/// <summary>
/// Репозиторий приоритетов
/// </summary>
public interface IPriorityRepository : IRepository<PriorityLevel>
{
    /// <summary>Получить все приоритеты отсортированные по Order</summary>
    List<PriorityLevel> GetAllOrdered();

    /// <summary>Получить самый важный (критический) приоритет</summary>
    PriorityLevel? GetHighestPriority();

    /// <summary>Получить приоритеты важнее указанного</summary>
    List<PriorityLevel> GetPrioritiesHigherThan(int priorityId);

    /// <summary>Переупорядочить приоритеты</summary>
    void Reorder(List<int> orderedIds);
}

/// <summary>
/// Репозиторий тегов
/// </summary>
public interface ITagRepository : IRepository<Tag>
{
    /// <summary>Найти тег по имени</summary>
    Tag? GetByName(string name);

    /// <summary>Получить или создать тег</summary>
    Tag GetOrCreate(string name);

    /// <summary>Получить популярные теги</summary>
    List<Tag> GetPopularTags(int count);

    /// <summary>Найти теги по части имени</summary>
    List<Tag> SearchByName(string query, int limit = 10);

    /// <summary>Обновить статистику использования тега</summary>
    void IncrementUsage(int tagId);
}

/// <summary>
/// Сервис алгоритмического планирования
/// </summary>
public interface IPlannerService
{
    /// <summary>Пересчитать приоритеты на основе таблиц повышения</summary>
    void ActualizePriorities(int dayStartHour);

    /// <summary>Распределить задачи по дням</summary>
    void DistributeTasks(UserSettings settings);

    /// <summary>Полный пересчёт: актуализация приоритетов + распределение</summary>
    void RecalculateAll(UserSettings settings);

    /// <summary>Проверить и обновить статусы заблокированных задач</summary>
    void UpdateBlockedStatuses();
}

/// <summary>
/// Сессионный сервис для тегов (хранит недавно использованные)
/// </summary>
public interface ITagSessionService
{
    /// <summary>Последний использованный тег в сессии</summary>
    Tag? LastUsedTag { get; }

    /// <summary>Отметить тег как использованный</summary>
    void MarkTagUsed(Tag tag);

    /// <summary>Получить рекомендуемые теги (последний + популярные)</summary>
    List<Tag> GetSuggestedTags(int count = 5);
}

/// <summary>
/// Хелпер для работы с датами с учётом времени начала дня
/// </summary>
public static class DateHelper
{
    /// <summary>
    /// Получить "логическую" дату с учётом времени начала дня.
    /// Если текущее время меньше времени начала дня, возвращает вчерашнюю дату.
    /// </summary>
    private static DateTime GetLogicalDate(DateTime dateTime, int dayStartHour)
    {
        if (dateTime.Hour >= 0 && dateTime.Hour < dayStartHour)
        {
            return dateTime.Date.AddDays(-1);
        }
        return dateTime.Date;
    }

    /// <summary>
    /// Получить сегодняшнюю "логическую" дату
    /// </summary>
    public static DateTime GetLogicalToday(int dayStartHour)
    {
        return GetLogicalDate(DateTime.Now, dayStartHour);
    }

    /// <summary>
    /// Проверить, является ли дата просроченной
    /// </summary>
    public static bool IsOverdue(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        var logicalToday = GetLogicalToday(dayStartHour);
        return date.Value < logicalToday;
    }

    /// <summary>
    /// Получить завтрашнюю "логическую" дату с учётом времени начала дня
    /// </summary>
    public static DateTime GetTomorrow(int dayStartHour)
    {
        return GetLogicalToday(dayStartHour).AddDays(1);
    }

    /// <summary>
    /// Проверить, является ли дата "сегодняшней" (логически)
    /// </summary>
    public static bool IsToday(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        return date.Value.Date == GetLogicalToday(dayStartHour);
    }

    /// <summary>
    /// Проверить, является ли дата "завтрашней" (логически)
    /// </summary>
    public static bool IsTomorrow(DateTime? date, int dayStartHour)
    {
        if (date == null) return false;
        return date.Value.Date == GetTomorrow(dayStartHour);
    }
}

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
