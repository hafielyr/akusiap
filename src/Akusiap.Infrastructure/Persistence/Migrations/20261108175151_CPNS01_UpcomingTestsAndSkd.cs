using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CPNS01_UpcomingTestsAndSkd : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "points",
                table: "question_options",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scoring_scheme",
                table: "exams",
                type: "text",
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "upcoming_tests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    organizer = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    registration_opens_on = table.Column<DateOnly>(type: "date", nullable: true),
                    test_starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    schedule_status = table.Column<string>(type: "text", nullable: false),
                    schedule_note = table.Column<string>(type: "text", nullable: true),
                    outline = table.Column<string>(type: "text", nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    exam_id = table.Column<long>(type: "bigint", nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upcoming_tests", x => x.id);
                    table.CheckConstraint("ck_upcoming_tests_category", "category IN ('Cpns', 'Pppk', 'Kedinasan', 'Bumn', 'TniPolri', 'Banking', 'Other')");
                    table.CheckConstraint("ck_upcoming_tests_schedule_status", "schedule_status IN ('Official', 'Estimated', 'NotAnnounced')");
                    table.ForeignKey(
                        name: "fk_upcoming_tests_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_question_options_points",
                table: "question_options",
                sql: "points BETWEEN 0 AND 5");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exams_scoring_scheme",
                table: "exams",
                sql: "scoring_scheme IN ('Standard', 'Skd')");

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_tests_exam_id",
                table: "upcoming_tests",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_tests_is_published_test_starts_on",
                table: "upcoming_tests",
                columns: new[] { "is_published", "test_starts_on" });

            migrationBuilder.CreateIndex(
                name: "ix_upcoming_tests_slug",
                table: "upcoming_tests",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upcoming_tests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_question_options_points",
                table: "question_options");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exams_scoring_scheme",
                table: "exams");

            migrationBuilder.DropColumn(
                name: "points",
                table: "question_options");

            migrationBuilder.DropColumn(
                name: "scoring_scheme",
                table: "exams");
        }
    }
}
