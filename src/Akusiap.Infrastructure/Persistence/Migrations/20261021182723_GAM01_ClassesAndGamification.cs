using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GAM01_ClassesAndGamification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "hide_on_leaderboard",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "classrooms",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    teacher_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    invite_code = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classrooms", x => x.id);
                    table.ForeignKey(
                        name: "fk_classrooms_users_teacher_user_id",
                        column: x => x.teacher_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    exam_id = table.Column<long>(type: "bigint", nullable: true),
                    awarded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badges", x => x.id);
                    table.CheckConstraint("ck_user_badges_code", "code IN ('Rank1', 'Top3', 'Top10', 'FirstSteps', 'Questions100', 'Questions500', 'Questions1000', 'Sharpshooter', 'PassedSimulation', 'Streak3', 'Streak7', 'Streak30', 'Flashcards50', 'Flashcards250', 'ClassJoined')");
                    table.ForeignKey(
                        name: "fk_user_badges_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_user_badges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "class_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    classroom_id = table.Column<long>(type: "bigint", nullable: false),
                    exam_id = table.Column<long>(type: "bigint", nullable: false),
                    rule_preset_id = table.Column<long>(type: "bigint", nullable: false),
                    opens_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_assignments", x => x.id);
                    table.CheckConstraint("ck_class_assignments_window", "closes_at > opens_at");
                    table.ForeignKey(
                        name: "fk_class_assignments_classrooms_classroom_id",
                        column: x => x.classroom_id,
                        principalTable: "classrooms",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_class_assignments_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_class_assignments_rule_presets_rule_preset_id",
                        column: x => x.rule_preset_id,
                        principalTable: "rule_presets",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "class_members",
                columns: table => new
                {
                    classroom_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_members", x => new { x.classroom_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_class_members_classrooms_classroom_id",
                        column: x => x.classroom_id,
                        principalTable: "classrooms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_class_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_assignments_classroom",
                table: "class_assignments",
                column: "classroom_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_assignments_exam",
                table: "class_assignments",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_assignments_rule_preset",
                table: "class_assignments",
                column: "rule_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_members_user",
                table: "class_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_classrooms_invite_code",
                table: "classrooms",
                column: "invite_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_classrooms_teacher",
                table: "classrooms",
                column: "teacher_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badges_exam_id",
                table: "user_badges",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ux_user_badges_once",
                table: "user_badges",
                columns: new[] { "user_id", "code", "exam_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.AddForeignKey(
                name: "fk_attempts_class_assignments_class_assignment_id",
                table: "attempts",
                column: "class_assignment_id",
                principalTable: "class_assignments",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_attempts_class_assignments_class_assignment_id",
                table: "attempts");

            migrationBuilder.DropTable(
                name: "class_assignments");

            migrationBuilder.DropTable(
                name: "class_members");

            migrationBuilder.DropTable(
                name: "user_badges");

            migrationBuilder.DropTable(
                name: "classrooms");

            migrationBuilder.DropColumn(
                name: "hide_on_leaderboard",
                table: "users");
        }
    }
}
