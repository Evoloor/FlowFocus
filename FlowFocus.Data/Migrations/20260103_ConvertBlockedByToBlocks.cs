using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    public partial class ConvertBlockedByToBlocks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Для всех записей с Type == BlockedBy (2), создаём/переносим их в запись с Source/Target поменяны и Type = Blocks (1).
            // Если уже существует запись Blocks с такими Source/Target, просто удаляем BlockedBy.

            // Обратите внимание: Sqlite не поддерживает сложные DML с JOIN в одном выражении, поэтому используем временную таблицу.

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS _temp_rel AS
                SELECT Id, SourceTaskId, TargetTaskId, Type FROM TaskRelations WHERE Type = 2;
            ");

            // Для каждой строки из временной таблицы: проверяем наличие обратной записи Blocks
            migrationBuilder.Sql(@"
                INSERT INTO TaskRelations (LastChangesOn, SourceTaskId, TargetTaskId, Type)
                SELECT datetime('now'), t.TargetTaskId, t.SourceTaskId, 1 FROM _temp_rel t
                WHERE NOT EXISTS(
                    SELECT 1 FROM TaskRelations tr WHERE tr.SourceTaskId = t.TargetTaskId AND tr.TargetTaskId = t.SourceTaskId AND tr.Type = 1
                );
            ");

            // Удаляем все исходные BlockedBy записи
            migrationBuilder.Sql(@"
                DELETE FROM TaskRelations WHERE Type = 2;
            ");

            // Удаляем временную таблицу
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS _temp_rel;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Обратная миграция сложная (данные утрачены при конвертации), оставим пустой Down.
        }
    }
}

