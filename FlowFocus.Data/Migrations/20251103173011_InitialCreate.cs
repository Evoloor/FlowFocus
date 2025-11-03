using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DayStartTime = table.Column<long>(type: "INTEGER", nullable: false),
                    DailyHoursLimit = table.Column<double>(type: "REAL", nullable: false),
                    DailyComplexityLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRecalculateOnAdd = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Interest = table.Column<int>(type: "INTEGER", nullable: false),
                    Complexity = table.Column<int>(type: "INTEGER", nullable: false),
                    Hours = table.Column<double>(type: "REAL", nullable: false),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AssignedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastChange = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    Repeat = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskBlockers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    BlockerTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskItemId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskBlockers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskBlockers_Tasks_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskBlockers_BlockerTaskId",
                table: "TaskBlockers",
                column: "BlockerTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBlockers_ParentTaskId",
                table: "TaskBlockers",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskBlockers_TaskItemId",
                table: "TaskBlockers",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedDate",
                table: "Tasks",
                column: "AssignedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_IsFavorite",
                table: "Tasks",
                column: "IsFavorite");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status",
                table: "Tasks",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "TaskBlockers");

            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
