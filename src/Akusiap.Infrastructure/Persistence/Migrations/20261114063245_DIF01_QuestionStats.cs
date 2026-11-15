using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DIF01_QuestionStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "was_flagged",
                table: "attempt_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "question_stats",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    wrong_count = table.Column<int>(type: "integer", nullable: false),
                    marked_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_stats", x => x.id);
                    table.CheckConstraint("ck_question_stats_correct_count", "correct_count >= 0");
                    table.CheckConstraint("ck_question_stats_marked_count", "marked_count >= 0");
                    table.CheckConstraint("ck_question_stats_wrong_count", "wrong_count >= 0");
                    table.ForeignKey(
                        name: "fk_question_stats_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_question_stats_question_id",
                table: "question_stats",
                column: "question_id",
                unique: true);

            // Existing sessions: an item marked when the session ended counts as marked (the history before was_flagged),
            // and finished non-flashcard sessions fill the counts (D-140).
            migrationBuilder.Sql("UPDATE attempt_items SET was_flagged = is_flagged WHERE is_flagged;");
            migrationBuilder.Sql("""
                INSERT INTO question_stats (question_id, correct_count, wrong_count, marked_count, updated_at)
                SELECT i.question_id,
                       count(*) FILTER (WHERE i.is_answered AND i.is_correct),
                       count(*) FILTER (WHERE i.is_answered AND NOT i.is_correct),
                       count(*) FILTER (WHERE i.was_flagged),
                       now()
                FROM attempt_items i
                JOIN attempts a ON a.id = i.attempt_id
                WHERE a.status <> 'InProgress' AND a.mode <> 'Flashcard'
                GROUP BY i.question_id
                HAVING count(*) FILTER (WHERE i.is_answered OR i.was_flagged) > 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "question_stats");

            migrationBuilder.DropColumn(
                name: "was_flagged",
                table: "attempt_items");
        }
    }
}
