using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DRL02_LeaderboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_user_badges_once",
                table: "user_badges");

            migrationBuilder.AddColumn<DateOnly>(
                name: "week_start",
                table: "user_badges",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "leaderboard_settings",
                columns: table => new
                {
                    exam_id = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    weekly_reset = table.Column<bool>(type: "boolean", nullable: false),
                    award_rank1 = table.Column<bool>(type: "boolean", nullable: false),
                    award_top3 = table.Column<bool>(type: "boolean", nullable: false),
                    award_top10 = table.Column<bool>(type: "boolean", nullable: false),
                    min_score_percent = table.Column<int>(type: "integer", nullable: true),
                    min_participants = table.Column<int>(type: "integer", nullable: true),
                    counts_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    weekly_awarded_through = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leaderboard_settings", x => x.exam_id);
                    table.CheckConstraint("ck_leaderboard_settings_min_participants", "min_participants IS NULL OR min_participants >= 1");
                    table.CheckConstraint("ck_leaderboard_settings_min_score", "min_score_percent IS NULL OR min_score_percent BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "fk_leaderboard_settings_asp_net_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_leaderboard_settings_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_user_badges_once",
                table: "user_badges",
                columns: new[] { "user_id", "code", "exam_id", "week_start" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_leaderboard_settings_updated_by_user_id",
                table: "leaderboard_settings",
                column: "updated_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "leaderboard_settings");

            migrationBuilder.DropIndex(
                name: "ux_user_badges_once",
                table: "user_badges");

            migrationBuilder.DropColumn(
                name: "week_start",
                table: "user_badges");

            migrationBuilder.CreateIndex(
                name: "ux_user_badges_once",
                table: "user_badges",
                columns: new[] { "user_id", "code", "exam_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }
    }
}
