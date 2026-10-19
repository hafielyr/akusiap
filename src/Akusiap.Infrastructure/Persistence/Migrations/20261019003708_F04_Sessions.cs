using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F04_Sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attempts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    guest_name = table.Column<string>(type: "text", nullable: true),
                    guest_token = table.Column<Guid>(type: "uuid", nullable: true),
                    exam_id = table.Column<long>(type: "bigint", nullable: true),
                    class_assignment_id = table.Column<long>(type: "bigint", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    mode = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    rules_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_saved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    time_spent_seconds = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    total_count = table.Column<int>(type: "integer", nullable: false),
                    score_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    passed = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempts", x => x.id);
                    table.CheckConstraint("ck_attempts_correct_count", "correct_count >= 0");
                    table.CheckConstraint("ck_attempts_correct_within_total", "correct_count <= total_count");
                    table.CheckConstraint("ck_attempts_exam_required", "source NOT IN ('Exam', 'Trial', 'Shared') OR exam_id IS NOT NULL");
                    table.CheckConstraint("ck_attempts_mode", "mode IN ('Practice', 'Simulation', 'Flashcard')");
                    table.CheckConstraint("ck_attempts_score_percent", "score_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_attempts_source", "source IN ('Exam', 'Trial', 'Shared', 'WrongOnly', 'Review', 'CustomMix')");
                    table.CheckConstraint("ck_attempts_status", "status IN ('InProgress', 'Submitted', 'Expired')");
                    table.CheckConstraint("ck_attempts_submitted_at", "status <> 'Submitted' OR submitted_at IS NOT NULL");
                    table.CheckConstraint("ck_attempts_taker", "user_id IS NOT NULL OR source = 'Trial' OR (source = 'Shared' AND guest_name IS NOT NULL)");
                    table.CheckConstraint("ck_attempts_time_spent_seconds", "time_spent_seconds >= 0");
                    table.CheckConstraint("ck_attempts_total_count", "total_count >= 0");
                    table.ForeignKey(
                        name: "fk_attempts_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_attempts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "content_reports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    question_id = table.Column<long>(type: "bigint", nullable: true),
                    exam_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_reports", x => x.id);
                    table.CheckConstraint("ck_content_reports_status", "status IN ('Open', 'Fixed', 'TakenDown', 'Rejected')");
                    table.CheckConstraint("ck_content_reports_target", "num_nonnulls(question_id, exam_id) >= 1");
                    table.ForeignKey(
                        name: "fk_content_reports_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_content_reports_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_content_reports_users_reporter_user_id",
                        column: x => x.reporter_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_content_reports_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    template_code = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    external_message_id = table.Column<string>(type: "text", nullable: true),
                    scheduled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_channel", "channel IN ('Email', 'InApp')");
                    table.CheckConstraint("ck_notifications_language", "language IN ('id', 'en')");
                    table.CheckConstraint("ck_notifications_status", "status IN ('Queued', 'Sent', 'Delivered', 'Read', 'Failed')");
                    table.CheckConstraint("ck_notifications_template_code", "template_code IN ('Receipt', 'Renewal7d', 'Renewal1d', 'RefundLink', 'ReviewDue')");
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "review_cards",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    interval_days = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    ease_factor = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
                    repetitions = table.Column<int>(type: "integer", nullable: false),
                    lapses = table.Column<int>(type: "integer", nullable: false),
                    last_reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_bookmarked = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_cards", x => x.id);
                    table.CheckConstraint("ck_review_cards_ease_factor", "ease_factor > 0");
                    table.CheckConstraint("ck_review_cards_interval_days", "interval_days >= 0");
                    table.CheckConstraint("ck_review_cards_lapses", "lapses >= 0");
                    table.CheckConstraint("ck_review_cards_repetitions", "repetitions >= 0");
                    table.CheckConstraint("ck_review_cards_state", "state IN ('New', 'Learning', 'Review', 'Memorized')");
                    table.ForeignKey(
                        name: "fk_review_cards_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_review_cards_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "attempt_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attempt_id = table.Column<long>(type: "bigint", nullable: false),
                    question_id = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    option_order = table.Column<string>(type: "jsonb", nullable: true),
                    is_answered = table.Column<bool>(type: "boolean", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    is_flagged = table.Column<bool>(type: "boolean", nullable: false),
                    hint_used = table.Column<bool>(type: "boolean", nullable: false),
                    time_spent_seconds = table.Column<int>(type: "integer", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_items", x => x.id);
                    table.CheckConstraint("ck_attempt_items_time_spent_seconds", "time_spent_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_attempt_items_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalTable: "attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_items_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "review_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    review_card_id = table.Column<long>(type: "bigint", nullable: false),
                    rating = table.Column<string>(type: "text", nullable: false),
                    interval_before_days = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    interval_after_days = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_logs", x => x.id);
                    table.CheckConstraint("ck_review_logs_rating", "rating IN ('Forgot', 'Hard', 'Good', 'Easy')");
                    table.ForeignKey(
                        name: "fk_review_logs_review_cards_review_card_id",
                        column: x => x.review_card_id,
                        principalTable: "review_cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attempt_item_options",
                columns: table => new
                {
                    attempt_item_id = table.Column<long>(type: "bigint", nullable: false),
                    question_option_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attempt_item_options", x => new { x.attempt_item_id, x.question_option_id });
                    table.ForeignKey(
                        name: "fk_attempt_item_options_attempt_items_attempt_item_id",
                        column: x => x.attempt_item_id,
                        principalTable: "attempt_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_attempt_item_options_question_options_question_option_id",
                        column: x => x.question_option_id,
                        principalTable: "question_options",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_attempt_item_options_question_option_id",
                table: "attempt_item_options",
                column: "question_option_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempt_items_attempt_id_position",
                table: "attempt_items",
                columns: new[] { "attempt_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attempt_items_attempt_id_question_id",
                table: "attempt_items",
                columns: new[] { "attempt_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attempt_items_question_id",
                table: "attempt_items",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_class_assignment_id",
                table: "attempts",
                column: "class_assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_exam_id",
                table: "attempts",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_guest_token",
                table: "attempts",
                column: "guest_token",
                filter: "guest_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attempts_user",
                table: "attempts",
                columns: new[] { "user_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_content_reports_exam_id",
                table: "content_reports",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_reports_open",
                table: "content_reports",
                column: "created_at",
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_content_reports_question_id",
                table: "content_reports",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_reports_reporter_user_id",
                table: "content_reports",
                column: "reporter_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_content_reports_resolved_by_user_id",
                table: "content_reports",
                column: "resolved_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_queue",
                table: "notifications",
                column: "scheduled_at",
                filter: "status = 'Queued'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user",
                table: "notifications",
                columns: new[] { "user_id", "scheduled_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_review_cards_due",
                table: "review_cards",
                columns: new[] { "user_id", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_review_cards_question_id",
                table: "review_cards",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_cards_user_id_question_id",
                table: "review_cards",
                columns: new[] { "user_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_logs_card",
                table: "review_logs",
                columns: new[] { "review_card_id", "reviewed_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attempt_item_options");

            migrationBuilder.DropTable(
                name: "content_reports");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "review_logs");

            migrationBuilder.DropTable(
                name: "attempt_items");

            migrationBuilder.DropTable(
                name: "review_cards");

            migrationBuilder.DropTable(
                name: "attempts");
        }
    }
}
