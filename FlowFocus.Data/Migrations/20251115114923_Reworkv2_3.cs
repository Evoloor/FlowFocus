using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reworkv2_3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    UserPriority = table.Column<int>(type: "INTEGER", nullable: false),
                    CalculatedPriority = table.Column<int>(type: "INTEGER", nullable: false),
                    Interest = table.Column<int>(type: "INTEGER", nullable: false),
                    Complexity = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedHours = table.Column<double>(type: "REAL", nullable: false),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRecurring = table.Column<bool>(type: "INTEGER", nullable: false),
                    Recurrence = table.Column<string>(type: "TEXT", nullable: true),
                    RecurrenceEndDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParentTaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    DisplayType = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DayStartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyTimeLimit = table.Column<double>(type: "REAL", nullable: false),
                    DailyComplexityLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRecalculateOnAdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFavorites = table.Column<bool>(type: "INTEGER", nullable: false),
                    PriorityBoostDates = table.Column<string>(type: "TEXT", nullable: true),
                    AutoCompleteGuaranteed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RemoveUrgentIfNotDone = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dependencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Logic = table.Column<string>(type: "TEXT", nullable: false),
                    ConditionParameters = table.Column<string>(type: "TEXT", nullable: true),
                    ConditionGroup = table.Column<string>(type: "TEXT", nullable: true),
                    ConditionOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dependencies", x => x.Id);
                    table.CheckConstraint("CK_Dependency_SelfReference", "SourceTaskId != TargetTaskId");
                    table.ForeignKey(
                        name: "FK_Dependencies_Tasks_SourceTaskId",
                        column: x => x.SourceTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Dependencies_Tasks_TargetTaskId",
                        column: x => x.TargetTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "UserSettings",
                columns: new[] { "Id", "AutoCompleteGuaranteed", "AutoRecalculateOnAdd", "DailyComplexityLimit", "DailyTimeLimit", "DayStartHour", "PriorityBoostDates", "RemoveUrgentIfNotDone", "ShowFavorites" },
                values: new object[] { 1, true, true, 50, 8.0, 6, null, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceTaskId",
                table: "Dependencies",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_TargetTaskId",
                table: "Dependencies",
                column: "TargetTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentTaskId",
                table: "Tasks",
                column: "ParentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PlannedDate",
                table: "Tasks",
                column: "PlannedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Status",
                table: "Tasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_UserPriority",
                table: "Tasks",
                column: "UserPriority");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Dependencies");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
