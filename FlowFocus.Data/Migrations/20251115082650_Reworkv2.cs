using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reworkv2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayStartHour = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyTimeLimit = table.Column<double>(type: "REAL", nullable: false),
                    DailyComplexityLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRecalculateOnAdd = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowFavorites = table.Column<bool>(type: "INTEGER", nullable: false),
                    PriorityBoostDates = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Dependencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetTaskId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    Logic = table.Column<string>(type: "TEXT", nullable: false),
                    ConditionParameters = table.Column<string>(type: "TEXT", nullable: true)
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
                columns: new[] { "Id", "AutoRecalculateOnAdd", "DailyComplexityLimit", "DailyTimeLimit", "DayStartHour", "PriorityBoostDates", "ShowFavorites" },
                values: new object[] { new Guid("d4763540-2c96-4a9b-be69-eeb84be25a87"), true, 50, 8.0, 6, null, true });

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_SourceTaskId",
                table: "Dependencies",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_Dependencies_TargetTaskId",
                table: "Dependencies",
                column: "TargetTaskId");

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
