using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CPNS07_TryoutSetsAndExamSale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "original_price_idr",
                table: "exams",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "tryout_set",
                table: "exam_questions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_exams_original_price_idr",
                table: "exams",
                sql: "original_price_idr IS NULL OR (price_idr IS NOT NULL AND original_price_idr > price_idr)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_exam_questions_tryout_set",
                table: "exam_questions",
                sql: "tryout_set BETWEEN 1 AND 10");

            // Owner update (2026-11-14) of the CPNS package: Rp39.000 (normally Rp80.000), 3 months of access, and no
            // KepmenPAN-RB paragraph. The tryout sets and the full description come from rerunning load-cpns-pack.
            migrationBuilder.Sql("""
                UPDATE exams
                SET price_idr = 39000, original_price_idr = 80000, access_days = 90,
                    description = split_part(description, E'\n\n', 1)
                WHERE owner_kind = 'App' AND title = 'Paket CPNS 2026: SKD';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exams_original_price_idr",
                table: "exams");

            migrationBuilder.DropCheckConstraint(
                name: "ck_exam_questions_tryout_set",
                table: "exam_questions");

            migrationBuilder.DropColumn(
                name: "original_price_idr",
                table: "exams");

            migrationBuilder.DropColumn(
                name: "tryout_set",
                table: "exam_questions");
        }
    }
}
