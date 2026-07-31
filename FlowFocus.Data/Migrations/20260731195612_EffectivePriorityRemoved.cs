using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class EffectivePriorityRemoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Priorities_EffectivePriorityId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_EffectivePriorityId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EffectivePriorityId",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EffectivePriorityId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_EffectivePriorityId",
                table: "Tasks",
                column: "EffectivePriorityId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Priorities_EffectivePriorityId",
                table: "Tasks",
                column: "EffectivePriorityId",
                principalTable: "Priorities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
