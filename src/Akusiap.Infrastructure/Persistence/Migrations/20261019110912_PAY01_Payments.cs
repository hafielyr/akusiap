using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PAY01_Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "promo_codes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    discount_amount_idr = table.Column<long>(type: "bigint", nullable: true),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_promo_codes", x => x.id);
                    table.CheckConstraint("ck_promo_codes_discount_amount_idr", "discount_amount_idr > 0");
                    table.CheckConstraint("ck_promo_codes_discount_percent", "discount_percent > 0 AND discount_percent <= 100");
                    table.CheckConstraint("ck_promo_codes_max_uses", "max_uses > 0");
                    table.CheckConstraint("ck_promo_codes_one_discount", "num_nonnulls(discount_percent, discount_amount_idr) = 1");
                    table.CheckConstraint("ck_promo_codes_used_count", "used_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "saved_payment_methods",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gateway = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    display_label = table.Column<string>(type: "text", nullable: false),
                    gateway_token = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_payment_methods", x => x.id);
                    table.CheckConstraint("ck_saved_payment_methods_gateway", "gateway = 'DOKU'");
                    table.CheckConstraint("ck_saved_payment_methods_type", "type IN ('CreditCard', 'DirectDebit')");
                    table.ForeignKey(
                        name: "fk_saved_payment_methods_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_number = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<long>(type: "bigint", nullable: true),
                    exam_purchase_id = table.Column<long>(type: "bigint", nullable: true),
                    promo_code_id = table.Column<long>(type: "bigint", nullable: true),
                    saved_payment_method_id = table.Column<long>(type: "bigint", nullable: true),
                    amount_idr = table.Column<long>(type: "bigint", nullable: false),
                    discount_idr = table.Column<long>(type: "bigint", nullable: false),
                    tax_idr = table.Column<long>(type: "bigint", nullable: false),
                    gateway_fee_idr = table.Column<long>(type: "bigint", nullable: true),
                    gateway = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: true),
                    va_number = table.Column<string>(type: "text", nullable: true),
                    qr_string = table.Column<string>(type: "text", nullable: true),
                    deeplink_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    invoice_number = table.Column<string>(type: "text", nullable: false),
                    gateway_request_id = table.Column<string>(type: "text", nullable: true),
                    is_late_payment = table.Column<bool>(type: "boolean", nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_idr", "amount_idr >= 0");
                    table.CheckConstraint("ck_payments_discount_idr", "discount_idr >= 0");
                    table.CheckConstraint("ck_payments_gateway", "gateway = 'DOKU'");
                    table.CheckConstraint("ck_payments_gateway_fee_idr", "gateway_fee_idr >= 0");
                    table.CheckConstraint("ck_payments_method", "method IN ('QRIS', 'VirtualAccount', 'EWallet', 'CreditCard')");
                    table.CheckConstraint("ck_payments_one_item", "num_nonnulls(subscription_id, exam_purchase_id) = 1");
                    table.CheckConstraint("ck_payments_paid_at", "status NOT IN ('Paid', 'Refunded') OR paid_at IS NOT NULL");
                    table.CheckConstraint("ck_payments_qr_string", "method <> 'QRIS' OR status <> 'Pending' OR qr_string IS NOT NULL");
                    table.CheckConstraint("ck_payments_retry_count", "retry_count >= 0");
                    table.CheckConstraint("ck_payments_status", "status IN ('Pending', 'Paid', 'Expired', 'Canceled', 'Failed', 'Refunded')");
                    table.CheckConstraint("ck_payments_tax_idr", "tax_idr >= 0");
                    table.CheckConstraint("ck_payments_va_number", "method <> 'VirtualAccount' OR status <> 'Pending' OR va_number IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_payments_exam_purchases_exam_purchase_id",
                        column: x => x.exam_purchase_id,
                        principalTable: "exam_purchases",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_promo_codes_promo_code_id",
                        column: x => x.promo_code_id,
                        principalTable: "promo_codes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_saved_payment_methods_saved_payment_method_id",
                        column: x => x.saved_payment_method_id,
                        principalTable: "saved_payment_methods",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_payments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_webhook_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    gateway = table.Column<string>(type: "text", nullable: false),
                    gateway_event_id = table.Column<string>(type: "text", nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: true),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    signature_valid = table.Column<bool>(type: "boolean", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_webhook_events", x => x.id);
                    table.CheckConstraint("ck_payment_webhook_events_gateway", "gateway = 'DOKU'");
                    table.ForeignKey(
                        name: "fk_payment_webhook_events_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    amount_idr = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    is_auto_approved = table.Column<bool>(type: "boolean", nullable: false),
                    refund_route = table.Column<string>(type: "text", nullable: true),
                    refund_link_url = table.Column<string>(type: "text", nullable: true),
                    refund_link_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    gateway_refund_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount_idr", "amount_idr > 0");
                    table.CheckConstraint("ck_refunds_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_refunds_link_required", "status <> 'AwaitingDetails' OR refund_link_url IS NOT NULL");
                    table.CheckConstraint("ck_refunds_refund_route", "refund_route IN ('DirectToPayerApp', 'RefundLink')");
                    table.CheckConstraint("ck_refunds_route_required", "status IN ('Requested', 'Rejected') OR refund_route IS NOT NULL");
                    table.CheckConstraint("ck_refunds_status", "status IN ('Requested', 'Approved', 'Rejected', 'AwaitingDetails', 'SentToDoku', 'Completed', 'Failed')");
                    table.ForeignKey(
                        name: "fk_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_refunds_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_gateway_gateway_event_id",
                table: "payment_webhook_events",
                columns: new[] { "gateway", "gateway_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_webhook_events_payment",
                table: "payment_webhook_events",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_number",
                table: "payments",
                column: "invoice_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_number",
                table: "payments",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_pending_deadline",
                table: "payments",
                column: "expires_at",
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_payments_promo",
                table: "payments",
                column: "promo_code_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_saved_method",
                table: "payments",
                column: "saved_payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_user",
                table: "payments",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_pending_exam_purchase",
                table: "payments",
                column: "exam_purchase_id",
                unique: true,
                filter: "status = 'Pending' AND exam_purchase_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_pending_subscription",
                table: "payments",
                column: "subscription_id",
                unique: true,
                filter: "status = 'Pending' AND subscription_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_decided_by",
                table: "refunds",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_open",
                table: "refunds",
                column: "status",
                filter: "status IN ('Requested', 'AwaitingDetails', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment",
                table: "refunds",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_payment_methods_user",
                table: "saved_payment_methods",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_subscriptions_saved_payment_methods_saved_payment_method_id",
                table: "subscriptions",
                column: "saved_payment_method_id",
                principalTable: "saved_payment_methods",
                principalColumn: "id");

            // Promo codes are unique ignoring case (an expression index EF cannot model).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_promo_codes_code ON promo_codes (upper(code));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_promo_codes_code;");

            migrationBuilder.DropForeignKey(
                name: "fk_subscriptions_saved_payment_methods_saved_payment_method_id",
                table: "subscriptions");

            migrationBuilder.DropTable(
                name: "payment_webhook_events");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "promo_codes");

            migrationBuilder.DropTable(
                name: "saved_payment_methods");
        }
    }
}
