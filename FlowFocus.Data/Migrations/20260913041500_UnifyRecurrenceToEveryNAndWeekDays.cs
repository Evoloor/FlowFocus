using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowFocus.Data.Migrations
{
    /// <inheritdoc />
    public partial class UnifyRecurrenceToEveryNAndWeekDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecurrenceUnit",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Legacy 1: Daily -> EveryN (1), Unit = Days (0), Interval = 1
            migrationBuilder.Sql("UPDATE Tasks SET RecurrenceType = 1, RecurrenceUnit = 0, RecurrenceInterval = 1 WHERE RecurrenceType = 1;");

            // Legacy 2: EveryNDays -> EveryN (1), Unit = Days (0), Interval = COALESCE(RecurrenceInterval, 1)
            migrationBuilder.Sql("UPDATE Tasks SET RecurrenceType = 1, RecurrenceUnit = 0, RecurrenceInterval = COALESCE(RecurrenceInterval, 1) WHERE RecurrenceType = 2;");

            // Legacy 4: Monthly -> EveryN (1), Unit = Months (1), Interval = COALESCE(RecurrenceInterval, 1)
            migrationBuilder.Sql("UPDATE Tasks SET RecurrenceType = 1, RecurrenceUnit = 1, RecurrenceInterval = COALESCE(RecurrenceInterval, 1) WHERE RecurrenceType = 4;");

            // Legacy 5: Yearly -> EveryN (1), Unit = Years (2), Interval = COALESCE(RecurrenceInterval, 1)
            migrationBuilder.Sql("UPDATE Tasks SET RecurrenceType = 1, RecurrenceUnit = 2, RecurrenceInterval = COALESCE(RecurrenceInterval, 1) WHERE RecurrenceType = 5;");

            // Ensure valid interval for EveryN
            migrationBuilder.Sql("UPDATE Tasks SET RecurrenceInterval = 1 WHERE RecurrenceType = 1 AND (RecurrenceInterval IS NULL OR RecurrenceInterval <= 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecurrenceUnit",
                table: "Tasks");
        }
    }
}
