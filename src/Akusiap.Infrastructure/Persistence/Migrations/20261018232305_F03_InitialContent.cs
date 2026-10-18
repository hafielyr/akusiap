using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Akusiap.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F03_InitialContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    plan_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    includes_all_exams = table.Column<bool>(type: "boolean", nullable: false),
                    includes_authoring = table.Column<bool>(type: "boolean", nullable: false),
                    includes_classes = table.Column<bool>(type: "boolean", nullable: false),
                    max_own_questions = table.Column<int>(type: "integer", nullable: false),
                    max_own_exams = table.Column<int>(type: "integer", nullable: false),
                    max_import_rows = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.plan_id);
                    table.CheckConstraint("ck_plans_code", "code IN ('Free', 'Learner', 'Teacher', 'Combined')");
                    table.CheckConstraint("ck_plans_max_import_rows", "max_import_rows >= 0");
                    table.CheckConstraint("ck_plans_max_own_exams", "max_own_exams >= 0");
                    table.CheckConstraint("ck_plans_max_own_questions", "max_own_questions >= 0");
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.role_id);
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    subject_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subjects", x => x.subject_id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preferred_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    whats_app_opt_in = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    deactivated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.user_id);
                    table.CheckConstraint("ck_users_deactivated_at", "is_active OR deactivated_at IS NOT NULL");
                    table.CheckConstraint("ck_users_preferred_language", "preferred_language IN ('id', 'en')");
                });

            migrationBuilder.CreateTable(
                name: "plan_prices",
                columns: table => new
                {
                    plan_price_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    duration_months = table.Column<int>(type: "integer", nullable: false),
                    price_idr = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_prices", x => x.plan_price_id);
                    table.UniqueConstraint("ak_plan_prices_plan_price_id_plan_id", x => new { x.plan_price_id, x.plan_id });
                    table.CheckConstraint("ck_plan_prices_duration_months", "duration_months IN (1, 3, 6, 12)");
                    table.CheckConstraint("ck_plan_prices_price_idr", "price_idr >= 0");
                    table.ForeignKey(
                        name: "fk_plan_prices_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topics",
                columns: table => new
                {
                    topic_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subject_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_topics", x => x.topic_id);
                    table.ForeignKey(
                        name: "fk_topics_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "subject_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    audit_log_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.audit_log_id);
                    table.ForeignKey(
                        name: "fk_audit_logs_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rule_presets",
                columns: table => new
                {
                    rule_preset_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_limit_minutes = table.Column<int>(type: "integer", nullable: true),
                    question_count = table.Column<int>(type: "integer", nullable: true),
                    hints_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    shuffle_questions = table.Column<bool>(type: "boolean", nullable: false),
                    shuffle_options = table.Column<bool>(type: "boolean", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: true),
                    pass_mark_percent = table.Column<int>(type: "integer", nullable: true),
                    show_answers = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_presets", x => x.rule_preset_id);
                    table.CheckConstraint("ck_rule_presets_max_attempts", "max_attempts > 0");
                    table.CheckConstraint("ck_rule_presets_pass_mark_percent", "pass_mark_percent BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_rule_presets_question_count", "question_count > 0");
                    table.CheckConstraint("ck_rule_presets_show_answers", "show_answers IN ('AfterEach', 'AfterSubmit', 'Never')");
                    table.CheckConstraint("ck_rule_presets_time_limit_minutes", "time_limit_minutes > 0");
                    table.ForeignKey(
                        name: "fk_rule_presets_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "role_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    subscription_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<int>(type: "integer", nullable: false),
                    plan_price_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    renewal_mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    saved_payment_method_id = table.Column<int>(type: "integer", nullable: true),
                    grace_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reminder7d_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reminder1d_sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    gateway_subscription_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.subscription_id);
                    table.CheckConstraint("ck_subscriptions_active_period", "status <> 'Active' OR current_period_end > current_period_start");
                    table.CheckConstraint("ck_subscriptions_auto_debit_method", "renewal_mode = 'Manual' OR saved_payment_method_id IS NOT NULL");
                    table.CheckConstraint("ck_subscriptions_renewal_mode", "renewal_mode IN ('Manual', 'AutoDebit')");
                    table.CheckConstraint("ck_subscriptions_status", "status IN ('PendingPayment', 'Active', 'Expired', 'Canceled')");
                    table.ForeignKey(
                        name: "fk_subscriptions_plan_prices_plan_price_id_plan_id",
                        columns: x => new { x.plan_price_id, x.plan_id },
                        principalTable: "plan_prices",
                        principalColumns: new[] { "plan_price_id", "plan_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questions",
                columns: table => new
                {
                    question_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    topic_id = table.Column<int>(type: "integer", nullable: false),
                    question_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    explanation = table.Column<string>(type: "text", nullable: false),
                    key_fact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    difficulty = table.Column<int>(type: "integer", nullable: false),
                    source_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    variant_group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.question_id);
                    table.CheckConstraint("ck_questions_difficulty", "difficulty BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_questions_question_type", "question_type IN ('SingleChoice', 'MultiSelect', 'TrueFalse')");
                    table.CheckConstraint("ck_questions_status", "status IN ('Draft', 'Active', 'Retired')");
                    table.ForeignKey(
                        name: "fk_questions_topics_topic_id",
                        column: x => x.topic_id,
                        principalTable: "topics",
                        principalColumn: "topic_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questions_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exams",
                columns: table => new
                {
                    exam_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<int>(type: "integer", nullable: true),
                    rule_preset_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    is_free = table.Column<bool>(type: "boolean", nullable: false),
                    price_idr = table.Column<long>(type: "bigint", nullable: true),
                    access_days = table.Column<int>(type: "integer", nullable: true),
                    own_question_allowance = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_shared = table.Column<bool>(type: "boolean", nullable: false),
                    share_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exams", x => x.exam_id);
                    table.CheckConstraint("ck_exams_access_days", "access_days > 0");
                    table.CheckConstraint("ck_exams_own_question_allowance", "own_question_allowance >= 0");
                    table.CheckConstraint("ck_exams_owner", "(owner_kind = 'App') = (owner_user_id IS NULL)");
                    table.CheckConstraint("ck_exams_owner_kind", "owner_kind IN ('App', 'Teacher', 'Learner')");
                    table.CheckConstraint("ck_exams_price_app_only", "owner_kind = 'App' OR price_idr IS NULL");
                    table.CheckConstraint("ck_exams_price_idr", "price_idr >= 0");
                    table.CheckConstraint("ck_exams_shared_has_token", "NOT is_shared OR share_token IS NOT NULL");
                    table.CheckConstraint("ck_exams_status", "status IN ('Draft', 'Published', 'Unpublished', 'TakenDown')");
                    table.ForeignKey(
                        name: "fk_exams_rule_presets_rule_preset_id",
                        column: x => x.rule_preset_id,
                        principalTable: "rule_presets",
                        principalColumn: "rule_preset_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exams_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "subject_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exams_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "question_options",
                columns: table => new
                {
                    question_option_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_options", x => x.question_option_id);
                    table.ForeignKey(
                        name: "fk_question_options_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exam_purchases",
                columns: table => new
                {
                    exam_purchase_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_id = table.Column<int>(type: "integer", nullable: false),
                    price_paid_idr = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    purchased_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    access_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_purchases", x => x.exam_purchase_id);
                    table.CheckConstraint("ck_exam_purchases_active_paid", "status <> 'Active' OR purchased_at IS NOT NULL");
                    table.CheckConstraint("ck_exam_purchases_price_paid_idr", "price_paid_idr >= 0");
                    table.CheckConstraint("ck_exam_purchases_status", "status IN ('PendingPayment', 'Active', 'Expired', 'Refunded')");
                    table.ForeignKey(
                        name: "fk_exam_purchases_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "exam_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_exam_purchases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exam_questions",
                columns: table => new
                {
                    exam_id = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_trial = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_questions", x => new { x.exam_id, x.question_id });
                    table.ForeignKey(
                        name: "fk_exam_questions_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "exam_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_questions_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "questions",
                        principalColumn: "question_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exam_purchases_exam_id",
                table: "exam_purchases",
                column: "exam_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_purchases_user_id",
                table: "exam_purchases",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exam_questions_question_id",
                table: "exam_questions",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_catalog",
                table: "exams",
                columns: new[] { "status", "subject_id" },
                filter: "owner_kind = 'App'");

            migrationBuilder.CreateIndex(
                name: "ix_exams_owner_user_id",
                table: "exams",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_rule_preset_id",
                table: "exams",
                column: "rule_preset_id");

            migrationBuilder.CreateIndex(
                name: "ix_exams_share_token",
                table: "exams",
                column: "share_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_exams_subject_id",
                table: "exams",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_prices_plan_id_duration_months",
                table: "plan_prices",
                columns: new[] { "plan_id", "duration_months" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_question_options_question_id",
                table: "question_options",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_owner_user_id",
                table: "questions",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_topic_id",
                table: "questions",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ix_questions_variant_group_id",
                table: "questions",
                column: "variant_group_id",
                filter: "variant_group_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rule_presets_owner_user_id",
                table: "rule_presets",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_rule_presets_one_system_default",
                table: "rule_presets",
                column: "is_default",
                unique: true,
                filter: "is_default AND owner_user_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_subjects_name",
                table: "subjects",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_current_period_end",
                table: "subscriptions",
                column: "current_period_end",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_price_id_plan_id",
                table: "subscriptions",
                columns: new[] { "plan_price_id", "plan_id" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_saved_payment_method_id",
                table: "subscriptions",
                column: "saved_payment_method_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_user_id",
                table: "subscriptions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_topics_subject_id_name",
                table: "topics",
                columns: new[] { "subject_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "exam_purchases");

            migrationBuilder.DropTable(
                name: "exam_questions");

            migrationBuilder.DropTable(
                name: "question_options");

            migrationBuilder.DropTable(
                name: "role_claims");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "exams");

            migrationBuilder.DropTable(
                name: "questions");

            migrationBuilder.DropTable(
                name: "plan_prices");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "rule_presets");

            migrationBuilder.DropTable(
                name: "topics");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "subjects");
        }
    }
}
