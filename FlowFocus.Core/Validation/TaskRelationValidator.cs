using FlowFocus.Core.Enums;
using FlowFocus.Core.Exceptions;
using FlowFocus.Core.Models;

namespace FlowFocus.Core.Validation;

/// <summary>
/// Валидатор связей между задачами
/// </summary>
public static class TaskRelationValidator
{
    public const int MaxRelationsPerTask = 15;

    /// <summary>
    /// Проверка допустимости создания новой связи между задачами
    /// </summary>
    public static void ValidateNewRelation(
        TaskItem sourceTask, 
        TaskItem targetTask, 
        RelationType type, 
        IEnumerable<TaskRelation>? existingRelationsGraph = null)
    {
        ArgumentNullException.ThrowIfNull(sourceTask);
        ArgumentNullException.ThrowIfNull(targetTask);

        // 1.3.2 / B 1.1: Запрет ссылки на себя
        if (sourceTask.Id != 0 && sourceTask.Id == targetTask.Id)
        {
            throw new InvalidOperationException("Задача не может быть связана сама с собой.");
        }

        // 1.3.3: Запрет связей с повторяющимися задачами
        if (sourceTask.IsRecurring || targetTask.IsRecurring)
        {
            throw new InvalidOperationException("Блокирующие и дочерние связи с повторяющимися задачами запрещены.");
        }

        // 1.3.4: Лимит количества связей (15 max)
        if (sourceTask.Relations.Count >= MaxRelationsPerTask)
        {
            throw new InvalidOperationException($"Достигнут лимит количества связей ({MaxRelationsPerTask}/{MaxRelationsPerTask}).");
        }

        // 2.1.1: Каскадная валидация приоритетов (Принцип блокера)
        if (type == RelationType.Blocks && sourceTask.Priority != null && targetTask.Priority != null)
        {
            if (sourceTask.Priority.Order > targetTask.Priority.Order)
            {
                throw new InvalidOperationException("Приоритет блокирующей задачи не может быть слабее приоритета блокируемой задачи.");
            }
        }

        // 2.1.3: Каскадная валидация сроков / дедлайнов
        if (type == RelationType.Blocks && sourceTask.ScheduledDate.HasValue && targetTask.ScheduledDate.HasValue)
        {
            if (sourceTask.ScheduledDate.Value.Date > targetTask.ScheduledDate.Value.Date)
            {
                throw new InvalidOperationException("Дедлайн блокирующей задачи не может быть позже дедлайна блокируемой задачи.");
            }
        }

        // B 1.1: Проверка на циклические блокировки
        if (type == RelationType.Blocks)
        {
            CheckCircularBlocks(sourceTask, targetTask, existingRelationsGraph);
        }
    }

    /// <summary>
    /// Проверка на циклический граф блокировок (A -> B -> C -> A)
    /// </summary>
    public static void CheckCircularBlocks(TaskItem source, TaskItem target, IEnumerable<TaskRelation>? relationsGraph = null)
    {
        HashSet<int> visited = [];
        Queue<int> queue = new();
        queue.Enqueue(target.Id);

        var graph = relationsGraph?.ToList() ?? [];

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (currentId == source.Id && source.Id != 0)
            {
                throw new CircularDependencyException($"Обнаружен циклический граф: создание связи от {source.Id} к {target.Id} приведет к зацикливанию.");
            }

            if (!visited.Add(currentId)) continue;

            // Найти задачи, которые заблокированы текущей задачей currentId (currentId blocks Next)
            var nextTaskIds = graph
                .Where(r => r.Type == RelationType.Blocks && r.SourceTaskId == currentId)
                .Select(r => r.TargetTaskId);

            foreach (var nextId in nextTaskIds)
            {
                queue.Enqueue(nextId);
            }
        }
    }
}
