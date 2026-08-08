using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExternalConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastChangesOn = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 9, nullable: false),
                    UsageCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUsedDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalConditions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    ConditionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskConditions_ExternalConditions_ConditionId",
                        column: x => x.ConditionId,
                        principalTable: "ExternalConditions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskConditions_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalConditions_Name",
                table: "ExternalConditions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskConditions_ConditionId",
                table: "TaskConditions",
                column: "ConditionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskConditions_TaskId",
                table: "TaskConditions",
                column: "TaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskConditions");

            migrationBuilder.DropTable(
                name: "ExternalConditions");
        }
    }
}
