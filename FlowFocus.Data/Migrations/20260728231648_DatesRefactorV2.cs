using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class DatesRefactorV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActualAssignedDate",
                table: "Tasks");

            migrationBuilder.RenameColumn(
                name: "UserAssignedDate",
                table: "Tasks",
                newName: "ScheduledDate");

            migrationBuilder.AddColumn<int>(
                name: "DateSource",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DateSource",
                table: "Tasks");

            migrationBuilder.RenameColumn(
                name: "ScheduledDate",
                table: "Tasks",
                newName: "UserAssignedDate");

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualAssignedDate",
                table: "Tasks",
                type: "TEXT",
                nullable: true);
        }
    }
}
