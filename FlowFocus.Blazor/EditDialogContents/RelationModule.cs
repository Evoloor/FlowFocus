using FlowFocus.Core.Models;
using FlowFocus.Core.Enums;

namespace FlowFocus.Blazor.EditDialogContents;

public static class RelationModule
{
    public static List<TaskRelation> SyncRelationsToTask(List<RelationDto> dtos, TaskItem task, TaskItem? existingTask)
    {
        // Отбрасываем невалидные записи: нет цели или цель является повторяющейся задачей (нельзя ссылаться на recurring)
        var validRelations = dtos.Where(r => r.TargetTask is not null && !r.TargetTask.IsRecurring).ToList();
        var existingRelationsSource = existingTask?.Relations ?? [];
        var existingRelationsInverse = existingTask?.InverseRelations ?? [];
        List<TaskRelation> resultRelations = [];

        foreach (var dto in validRelations)
        {
            // Защитное получение id цели
            var targetId = dto.TargetTask?.Id ?? 0;
            if (targetId == 0)
            {
                // Пропускаем некорректную запись
                continue;
            }

            // Нормализуем типы связей для хранения в БД:
            // - Для логики блокировок в БД храним только RelationType.Blocks, где SourceTask блокирует TargetTask.
            // - Если пользователь указал BlockedBy (т.е. текущая задача блокируется другой), то в БД сохраняем запись с Source = другая задача, Target = текущая задача и Type = Blocks.
            // - Если пользователь указал Blocks (текущая задача блокирует другую), то в БД сохраняем запись с Source = текущая задача, Target = другая и Type = Blocks.
            // - Для прочих типов (RelatedTo, Subtask) сохраняем тип как есть, с Source = текущая задача.

            // Определяем желаемую целевую запись в БД
            RelationType dbType;
            int dbSourceId;
            int dbTargetId;

            switch (dto.Type)
            {
                case RelationType.BlockedBy:
                    // DTO означает: текущая задача блокируется указанной TargetTask
                    dbType = RelationType.Blocks;
                    dbSourceId = targetId; // Другой таск является source
                    dbTargetId = task.Id > 0 ? task.Id : 0; // Текущий таск является целью
                    break;
                case RelationType.Blocks:
                    dbType = RelationType.Blocks;
                    dbSourceId = task.Id > 0 ? task.Id : 0;
                    dbTargetId = targetId;
                    break;
                case RelationType.RelatedTo:
                case RelationType.Subtask:
                default:
                    // Обычные типы сохраняем напрямую (Source = текущая задача)
                    dbType = dto.Type;
                    dbSourceId = task.Id > 0 ? task.Id : 0;
                    dbTargetId = targetId;
                    break;
            }

            if (dto.Id is > 0)
            {
                var existing = existingRelationsSource.FirstOrDefault(r => r.Id == dto.Id.Value)
                               ?? existingRelationsInverse.FirstOrDefault(r => r.Id == dto.Id.Value);
                if (existing != null)
                {
                    TaskRelation updatedRelation = new()
                    {
                        Id = existing.Id,
                        SourceTaskId = dbSourceId != 0 ? dbSourceId : existing.SourceTaskId,
                        TargetTaskId = dbTargetId,
                        Type = dbType,
                        LastChangesOn = DateTime.UtcNow
                    };
                    resultRelations.Add(updatedRelation);
                    continue;
                }
            }

            // Добавляем новую запись
            TaskRelation newRelation = new()
            {
                SourceTaskId = dbSourceId,
                TargetTaskId = dbTargetId,
                Type = dbType,
                LastChangesOn = DateTime.UtcNow
            };
            resultRelations.Add(newRelation);
        }

        return resultRelations;
    }
}