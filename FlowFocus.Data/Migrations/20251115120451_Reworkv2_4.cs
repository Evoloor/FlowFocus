using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reworkv2_4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoBalanceTasks",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CustomPriorityReferences",
                table: "UserSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxComplexTasksPerDay",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShowProcrastinationButton",
                table: "UserSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsProcrastinationResistant",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastProcrastinatedDate",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcrastinationCount",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "UserSettings",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AutoBalanceTasks", "CustomPriorityReferences", "MaxComplexTasksPerDay", "ShowProcrastinationButton" },
                values: new object[] { true, null, 3, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoBalanceTasks",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "CustomPriorityReferences",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "MaxComplexTasksPerDay",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "ShowProcrastinationButton",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "IsProcrastinationResistant",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "LastProcrastinatedDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ProcrastinationCount",
                table: "Tasks");
        }
    }
}
