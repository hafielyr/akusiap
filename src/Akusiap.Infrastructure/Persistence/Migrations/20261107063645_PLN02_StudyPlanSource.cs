using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PLN02_StudyPlanSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts",
                sql: "source IN ('Exam', 'Trial', 'Shared', 'WrongOnly', 'Review', 'CustomMix', 'DailyDrill', 'StudyPlan')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts",
                sql: "source IN ('Exam', 'Trial', 'Shared', 'WrongOnly', 'Review', 'CustomMix', 'DailyDrill')");
        }
    }
}
