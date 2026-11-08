using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ADMT8_QuestionOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_url",
                table: "questions",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_questions_origin",
                table: "questions",
                sql: "origin IN ('Original', 'ThirdParty')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_questions_origin",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "questions");

            migrationBuilder.DropColumn(
                name: "source_url",
                table: "questions");
        }
    }
}
