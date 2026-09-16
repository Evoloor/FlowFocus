using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class SplitSubtasksToSeparateTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Subtasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    LastChangesOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 5000, nullable: true),
                    HideUnderSpoiler = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    Interest = table.Column<int>(type: "INTEGER", nullable: true),
                    Complexity = table.Column<int>(type: "INTEGER", nullable: true),
                    EstimatedMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Subtasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Subtasks_Tasks_ParentTaskId",
                        column: x => x.ParentTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Subtasks_ParentTaskId",
                table: "Subtasks",
                column: "ParentTaskId");

            // Migrate existing subtasks data from Tasks into Subtasks
            migrationBuilder.Sql(@"
                INSERT INTO Subtasks (Id, ParentTaskId, SortOrder, LastChangesOn, Title, Description, HideUnderSpoiler, Status, IsFavorite, Interest, Complexity, EstimatedMinutes, CompletedDate, CreatedDate)
                SELECT Id, ParentTaskId, 0, LastChangesOn, Title, Description, HideUnderSpoiler, Status, IsFavorite, Interest, Complexity, EstimatedMinutes, CompletedDate, CreatedDate
                FROM Tasks
                WHERE ParentTaskId IS NOT NULL;
            ");

            // Delete subtasks from Tasks table
            migrationBuilder.Sql("DELETE FROM Tasks WHERE ParentTaskId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Tasks_ParentTaskId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ParentTaskId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ParentTaskId",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParentTaskId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Restore subtasks back into Tasks table
            migrationBuilder.Sql(@"
                INSERT INTO Tasks (Id, ParentTaskId, LastChangesOn, Title, Description, HideUnderSpoiler, Status, IsFavorite, Interest, Complexity, EstimatedMinutes, CompletedDate, CreatedDate, DateSource, RecurrenceType, RecurrenceUnit)
                SELECT Id, ParentTaskId, LastChangesOn, Title, Description, HideUnderSpoiler, Status, IsFavorite, Interest, Complexity, EstimatedMinutes, CompletedDate, CreatedDate, 0, 0, 0
                FROM Subtasks;
            ");

            migrationBuilder.DropTable(
                name: "Subtasks");
        }
    }
}
