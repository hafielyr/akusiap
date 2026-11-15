using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// FIX-04 (D-143): the CPNS package's preset shows answers after each question, so Latihan gets "Periksa" and the
    /// explanation per question. Only Practice reads it; the tryout keeps showing answers after submitting.
    /// </summary>
    public partial class FIX04_CpnsPresetAnswersAfterEach : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE rule_presets SET show_answers = 'AfterEach'
                WHERE owner_user_id IS NULL AND name = 'Tryout SKD CPNS' AND show_answers = 'AfterSubmit';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE rule_presets SET show_answers = 'AfterSubmit'
                WHERE owner_user_id IS NULL AND name = 'Tryout SKD CPNS' AND show_answers = 'AfterEach';
                """);
        }
    }
}
