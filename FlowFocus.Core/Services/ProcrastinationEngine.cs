using FlowFocus.Core.Models;

namespace FlowFocus.Core.Services;

/// <summary>
/// Движок подбора идеальной задачи для прокрастинации и вытеснения
/// </summary>
public static class ProcrastinationEngine
{
    /// <summary>
    /// Расчет идеальной задачи для прокрастинации:
    /// Выбирается задача с Interestingness > 7, имеющая максимальный результат выражения: Interestingness - sqrt(Priority.Order).
    /// </summary>
    public static TaskItem? SelectIdealProcrastinationTask(IEnumerable<TaskItem> tasks)
    {
        return tasks
            .Where(t => (t.Interest ?? 0) > 7)
            .OrderByDescending(t => (double)(t.Interest ?? 0) - Math.Sqrt(t.Priority?.Order ?? 99))
            .FirstOrDefault();
    }

    /// <summary>
    /// Проверка необходимости вытеснения задач при исчерпании дневного лимита
    /// </summary>
    public static bool RequiresDisplacement(int currentScheduledMinutes, int dailyTimeLimit)
    {
        return currentScheduledMinutes >= dailyTimeLimit;
    }

    /// <summary>
    /// Находит наименее приоритетное дело дня для предложения замены при вытеснении
    /// </summary>
    public static TaskItem? FindLeastPriorityTaskForDisplacement(IEnumerable<TaskItem> todaysTasks)
    {
        return todaysTasks
            .OrderByDescending(t => t.Priority?.Order ?? 99)
            .ThenBy(t => t.Interest ?? 0)
            .FirstOrDefault();
    }
}
