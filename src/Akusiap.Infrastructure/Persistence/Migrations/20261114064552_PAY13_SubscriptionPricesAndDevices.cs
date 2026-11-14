using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PAY13_SubscriptionPricesAndDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "extra_devices",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "pending_extra_devices",
                table: "subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_price_idr",
                table: "plan_prices",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_devices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_key = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_sign_in_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    pushed_out = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_devices_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_extra_devices",
                table: "subscriptions",
                sql: "extra_devices BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_pending_extra_devices",
                table: "subscriptions",
                sql: "pending_extra_devices IS NULL OR pending_extra_devices BETWEEN 0 AND 4");

            migrationBuilder.AddCheckConstraint(
                name: "ck_plan_prices_original_price_idr",
                table: "plan_prices",
                sql: "original_price_idr IS NULL OR original_price_idr > price_idr");

            migrationBuilder.CreateIndex(
                name: "ix_user_devices_user_id_device_key",
                table: "user_devices",
                columns: new[] { "user_id", "device_key" },
                unique: true);

            // Owner prices (2026-11-14) for the Learner plan, the subscription on /harga: Rp20.000 a month and Rp110.000 a
            // year (normally Rp200.000). The 3- and 6-month packages go off sale; /admin/paket can change all of this.
            migrationBuilder.Sql("""
                INSERT INTO plan_prices (plan_id, duration_months, price_idr, original_price_idr, is_active)
                SELECT id, 1, 20000, NULL, true FROM plans WHERE code = 'Learner'
                ON CONFLICT (plan_id, duration_months) DO UPDATE SET price_idr = 20000, original_price_idr = NULL, is_active = true;
                INSERT INTO plan_prices (plan_id, duration_months, price_idr, original_price_idr, is_active)
                SELECT id, 12, 110000, 200000, true FROM plans WHERE code = 'Learner'
                ON CONFLICT (plan_id, duration_months) DO UPDATE SET price_idr = 110000, original_price_idr = 200000, is_active = true;
                UPDATE plan_prices SET is_active = false
                WHERE duration_months IN (3, 6) AND plan_id IN (SELECT id FROM plans WHERE code = 'Learner');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_devices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_extra_devices",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_pending_extra_devices",
                table: "subscriptions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_plan_prices_original_price_idr",
                table: "plan_prices");

            migrationBuilder.DropColumn(
                name: "extra_devices",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "pending_extra_devices",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "original_price_idr",
                table: "plan_prices");
        }
    }
}
