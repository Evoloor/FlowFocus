using FlowFocus.Core.Models;
using FlowFocus.Core.Enums;

namespace FlowFocus.Blazor.EditDialogContents;

public static class RelationModule
{
    public static (List<TaskRelation> Outgoing, List<TaskRelation> Incoming) SyncRelationsToTask(List<RelationDto> dtos, TaskItem task, TaskItem? existingTask)
    {
        // Отбрасываем невалидные записи: нет цели или цель является повторяющейся задачей (нельзя ссылаться на recurring)
        var validRelations = dtos.Where(r => r.TargetTask is not null && !r.TargetTask.IsRecurring).ToList();
        var existingRelationsSource = existingTask?.Relations ?? [];
        var existingRelationsInverse = existingTask?.InverseRelations ?? [];

        List<TaskRelation> outgoingRelations = [];
        List<TaskRelation> incomingRelations = [];

        foreach (var dto in validRelations)
        {
            // Защитное получение id цели
            var targetId = dto.TargetTask?.Id ?? 0;
            if (targetId == 0)
            {
                // Пропускаем некорректную запись
                continue;
            }

            // Исключаем связывание задачи с самой собой
            if (task.Id > 0 && targetId == task.Id)
            {
                continue;
            }

            RelationType dbType;
            int dbSourceId;
            int dbTargetId;
            bool isIncoming;

            switch (dto.Type)
            {
                case RelationType.BlockedBy:
                    // DTO означает: текущая задача блокируется указанной TargetTask
                    dbType = RelationType.Blocks;
                    dbSourceId = targetId; // Другой таск является source
                    dbTargetId = task.Id > 0 ? task.Id : 0; // Текущий таск является целью
                    isIncoming = true;
                    break;
                case RelationType.Blocks:
                    dbType = RelationType.Blocks;
                    dbSourceId = task.Id > 0 ? task.Id : 0;
                    dbTargetId = targetId;
                    isIncoming = false;
                    break;
                case RelationType.RelatedTo:
                case RelationType.Subtask:
                default:
                    // Обычные типы сохраняем напрямую (Source = текущая задача)
                    dbType = dto.Type;
                    dbSourceId = task.Id > 0 ? task.Id : 0;
                    dbTargetId = targetId;
                    isIncoming = false;
                    break;
            }

            if (dto.Id is > 0)
            {
                var existingInSource = existingRelationsSource.FirstOrDefault(r => r.Id == dto.Id.Value);
                var existingInInverse = existingRelationsInverse.FirstOrDefault(r => r.Id == dto.Id.Value);
                var existing = existingInSource ?? existingInInverse;

                if (existing != null)
                {
                    int finalSourceId;
                    int finalTargetId;
                    RelationType finalType;

                    if (existingInInverse != null)
                    {
                        finalSourceId = existing.SourceTaskId;
                        finalTargetId = existing.TargetTaskId;
                        finalType = existing.Type;
                    }
                    else
                    {
                        finalSourceId = dbSourceId != 0 ? dbSourceId : existing.SourceTaskId;
                        finalTargetId = dbTargetId != 0 ? dbTargetId : existing.TargetTaskId;
                        finalType = dbType;
                    }

                    if (finalSourceId > 0 && finalSourceId == finalTargetId)
                    {
                        continue;
                    }

                    TaskRelation updatedRelation = new()
                    {
                        Id = existing.Id,
                        SourceTaskId = finalSourceId,
                        TargetTaskId = finalTargetId,
                        Type = finalType,
                        LastChangesOn = DateTime.UtcNow
                    };

                    if (isIncoming)
                        incomingRelations.Add(updatedRelation);
                    else
                        outgoingRelations.Add(updatedRelation);

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

            if (isIncoming)
                incomingRelations.Add(newRelation);
            else
                outgoingRelations.Add(newRelation);
        }

        return (outgoingRelations, incomingRelations);
    }
}