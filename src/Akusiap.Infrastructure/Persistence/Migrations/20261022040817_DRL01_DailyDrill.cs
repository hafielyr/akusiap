using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DRL01_DailyDrill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_badges_code",
                table: "user_badges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_badges_code",
                table: "user_badges",
                sql: "code IN ('Rank1', 'Top3', 'Top10', 'FirstSteps', 'Questions100', 'Questions500', 'Questions1000', 'Sharpshooter', 'PassedSimulation', 'Streak3', 'Streak7', 'Streak30', 'Flashcards50', 'Flashcards250', 'ClassJoined', 'DailyGoal7', 'DailyGoal30')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts",
                sql: "source IN ('Exam', 'Trial', 'Shared', 'WrongOnly', 'Review', 'CustomMix', 'DailyDrill')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_badges_code",
                table: "user_badges");

            migrationBuilder.DropCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_badges_code",
                table: "user_badges",
                sql: "code IN ('Rank1', 'Top3', 'Top10', 'FirstSteps', 'Questions100', 'Questions500', 'Questions1000', 'Sharpshooter', 'PassedSimulation', 'Streak3', 'Streak7', 'Streak30', 'Flashcards50', 'Flashcards250', 'ClassJoined')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_attempts_source",
                table: "attempts",
                sql: "source IN ('Exam', 'Trial', 'Shared', 'WrongOnly', 'Review', 'CustomMix')");
        }
    }
}
