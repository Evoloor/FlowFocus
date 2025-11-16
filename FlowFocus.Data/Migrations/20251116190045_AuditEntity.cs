using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangesOn",
                table: "UserSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangesOn",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangesOn",
                table: "Dependencies",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "UserSettings",
                keyColumn: "Id",
                keyValue: 1,
                column: "LastChangesOn",
                value: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastChangesOn",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "LastChangesOn",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "LastChangesOn",
                table: "Dependencies");
        }
    }
}
