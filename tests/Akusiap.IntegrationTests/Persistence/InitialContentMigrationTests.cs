using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>F-03: the migration builds every ERD table for content and access on an empty database.</summary>
public sealed class InitialContentMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc);

    // ERD columns (snake_case) per table: "column type" with "?" for nullable. Types use PostgreSQL udt names.
    private static readonly Dictionary<string, string[]> ErdColumns = new()
    {
        ["users"] =
        [
            "user_id uuid", "email varchar", "phone_number text?", "email_confirmed bool", "password_hash text?",
            "display_name varchar", "preferred_language varchar", "whats_app_opt_in bool", "is_active bool",
            "deactivated_at timestamptz?", "created_at timestamptz",
        ],
        ["roles"] = ["role_id uuid", "name varchar"],
        ["user_roles"] = ["user_id uuid", "role_id uuid"],
        ["subjects"] = ["subject_id int4", "name varchar", "description text"],
        ["topics"] = ["topic_id int4", "subject_id int4", "name varchar", "description text"],
        ["questions"] =
        [
            "question_id int4", "owner_user_id uuid?", "topic_id int4", "question_type varchar", "text text",
            "explanation text", "key_fact varchar", "hint text?", "difficulty int4", "source_note varchar",
            "variant_group_id uuid?", "status varchar", "created_at timestamptz", "updated_at timestamptz",
        ],
        ["question_options"] = ["question_option_id int4", "question_id int4", "text text", "is_correct bool", "sort_order int4"],
        ["exams"] =
        [
            "exam_id int4", "owner_user_id uuid?", "owner_kind varchar", "subject_id int4?", "rule_preset_id int4",
            "title varchar", "description text", "is_free bool", "price_idr int8?", "access_days int4?",
            "own_question_allowance int4", "status varchar", "is_shared bool", "share_token varchar?", "created_at timestamptz",
        ],
        ["exam_questions"] = ["exam_id int4", "question_id int4", "sort_order int4", "is_trial bool"],
        ["rule_presets"] =
        [
            "rule_preset_id int4", "owner_user_id uuid?", "name varchar", "time_limit_minutes int4?", "question_count int4?",
            "hints_enabled bool", "shuffle_questions bool", "shuffle_options bool", "max_attempts int4?",
            "pass_mark_percent int4?", "show_answers varchar", "is_default bool",
        ],
        ["plans"] =
        [
            "plan_id int4", "code varchar", "name varchar", "includes_all_exams bool", "includes_authoring bool",
            "includes_classes bool", "max_own_questions int4", "max_own_exams int4", "max_import_rows int4", "is_active bool",
        ],
        ["plan_prices"] = ["plan_price_id int4", "plan_id int4", "duration_months int4", "price_idr int8", "is_active bool"],
        ["subscriptions"] =
        [
            "subscription_id int4", "user_id uuid", "plan_id int4", "plan_price_id int4", "status varchar",
            "current_period_start timestamptz", "current_period_end timestamptz", "renewal_mode varchar",
            "saved_payment_method_id int4?", "grace_until timestamptz?", "reminder7d_sent_at timestamptz?",
            "reminder1d_sent_at timestamptz?", "gateway_subscription_id varchar?", "created_at timestamptz",
        ],
        ["exam_purchases"] =
        [
            "exam_purchase_id int4", "user_id uuid", "exam_id int4", "price_paid_idr int8", "status varchar",
            "purchased_at timestamptz?", "access_expires_at timestamptz",
        ],
        ["audit_logs"] =
        [
            "audit_log_id int4", "actor_user_id uuid", "action varchar", "entity_name varchar", "entity_id varchar",
            "changes_json jsonb", "created_at timestamptz",
        ],
    };

    private TemporaryDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await TemporaryDatabase.CreateAsync();
        await using var db = _database.CreateContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Migration_applies_to_an_empty_database()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Contains("F03_InitialContent", (await db.Database.GetAppliedMigrationsAsync()).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_erd_column_exists_with_the_right_type_and_nullability()
    {
        await using var db = _database.CreateContext();
        var actual = await db.Database
            .SqlQueryRaw<ColumnInfo>(
                "SELECT table_name AS \"Table\", column_name AS \"Column\", udt_name AS \"Type\", is_nullable = 'YES' AS \"Nullable\" " +
                "FROM information_schema.columns WHERE table_schema = 'public'")
            .ToListAsync();
        var columns = actual.ToDictionary(c => (c.Table, c.Column));

        var problems = new List<string>();
        foreach (var (table, expectedColumns) in ErdColumns)
        {
            foreach (var spec in expectedColumns)
            {
                var parts = spec.Split(' ');
                var nullable = parts[1].EndsWith('?');
                var type = parts[1].TrimEnd('?');
                if (!columns.TryGetValue((table, parts[0]), out var column))
                {
                    problems.Add($"{table}.{parts[0]} is missing");
                }
                else if (column.Type != type || column.Nullable != nullable)
                {
                    problems.Add($"{table}.{parts[0]} is {column.Type}{(column.Nullable ? "?" : "")}, expected {parts[1]}");
                }
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task Required_unique_indexes_exist()
    {
        await using var db = _database.CreateContext();
        var uniqueIndexes = await db.Database
            .SqlQueryRaw<string>(
                "SELECT tablename || '(' || array_to_string(array_agg(a.attname ORDER BY k.ord), ',') || ')' AS \"Value\" " +
                "FROM pg_indexes p JOIN pg_class c ON c.relname = p.indexname JOIN pg_index i ON i.indexrelid = c.oid " +
                "CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord) " +
                "JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = k.attnum " +
                "WHERE p.schemaname = 'public' AND i.indisunique GROUP BY tablename, indexname")
            .ToListAsync();

        Assert.Contains("users(email)", uniqueIndexes);
        Assert.Contains("exams(share_token)", uniqueIndexes);
        Assert.Contains("exam_questions(exam_id,question_id)", uniqueIndexes);
        Assert.Contains("plans(code)", uniqueIndexes);
    }

    [Fact]
    public async Task One_of_each_entity_round_trips()
    {
        var userId = Guid.NewGuid();
        await SeedOneOfEachAsync(userId);

        await using (var db = _database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            Assert.Equal("Budi", user.DisplayName);
            Assert.Equal(Languages.Indonesian, user.PreferredLanguage);
            Assert.True(user.IsActive);
            Assert.Equal(Now, user.CreatedAt);
            Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
            Assert.Single(await db.UserRoles.Where(ur => ur.UserId == userId).ToListAsync());

            var exam = await db.Exams
                .Include(e => e.Subject)
                .Include(e => e.RulePreset)
                .Include(e => e.Questions).ThenInclude(eq => eq.Question!).ThenInclude(q => q.Options)
                .Include(e => e.Questions).ThenInclude(eq => eq.Question!).ThenInclude(q => q.Topic)
                .SingleAsync();
            Assert.Equal(ExamStatus.Published, exam.Status);
            Assert.Equal(OwnerKind.App, exam.OwnerKind);
            Assert.Equal(49_000, exam.PriceIdr);
            Assert.Equal("CPNS", exam.Subject!.Name);
            Assert.Equal(ShowAnswers.AfterEach, exam.RulePreset!.ShowAnswers);
            var examQuestion = Assert.Single(exam.Questions);
            Assert.True(examQuestion.IsTrial);
            Assert.Equal("TWK", examQuestion.Question!.Topic!.Name);
            Assert.Equal(2, examQuestion.Question.Options.Count);
            Assert.Single(examQuestion.Question.Options, o => o.IsCorrect);

            var subscription = await db.Subscriptions.Include(s => s.Plan).Include(s => s.PlanPrice).SingleAsync();
            Assert.Equal(SubscriptionStatus.Active, subscription.Status);
            Assert.Equal(RenewalMode.Manual, subscription.RenewalMode);
            Assert.Equal(PlanCode.Learner, subscription.Plan!.Code);
            Assert.Equal(129_000, subscription.PlanPrice!.PriceIdr);

            var purchase = await db.ExamPurchases.SingleAsync();
            Assert.Equal(ExamPurchaseStatus.Active, purchase.Status);
            Assert.Equal(Now.AddDays(30), purchase.AccessExpiresAt);

            var audit = await db.AuditLogs.SingleAsync();
            Assert.Contains("Published", audit.ChangesJson, StringComparison.Ordinal);

            // Enums are stored as their names.
            var storedStatus = await db.Database.SqlQueryRaw<string>("SELECT status AS \"Value\" FROM exams").SingleAsync();
            Assert.Equal("Published", storedStatus);
        }
    }

    [Fact]
    public async Task Database_rejects_rows_that_break_the_rules()
    {
        await SeedOneOfEachAsync(Guid.NewGuid());
        (string Sql, string Constraint)[] cases =
        [
            ("UPDATE exams SET status = 'Archived'", "ck_exams_status"),
            ("UPDATE exams SET owner_kind = 'Learner', price_idr = NULL", "ck_exams_owner"),
            ("UPDATE exams SET is_shared = true, share_token = NULL", "ck_exams_shared_has_token"),
            ("UPDATE exams SET price_idr = -1", "ck_exams_price_idr"),
            ("UPDATE users SET preferred_language = 'fr'", "ck_users_preferred_language"),
            ("UPDATE users SET is_active = false", "ck_users_deactivated_at"),
            ("UPDATE questions SET difficulty = 6", "ck_questions_difficulty"),
            ("UPDATE rule_presets SET pass_mark_percent = 101", "ck_rule_presets_pass_mark_percent"),
            ("UPDATE plan_prices SET duration_months = 2", "ck_plan_prices_duration_months"),
            ("UPDATE subscriptions SET renewal_mode = 'AutoDebit'", "ck_subscriptions_auto_debit_method"),
            ("UPDATE subscriptions SET current_period_end = current_period_start", "ck_subscriptions_active_period"),
            ("UPDATE exam_purchases SET purchased_at = NULL", "ck_exam_purchases_active_paid"),
            ("INSERT INTO subjects (name, description) VALUES ('CPNS', '')", "ix_subjects_name"),
            (
                "INSERT INTO rule_presets (name, hints_enabled, shuffle_questions, shuffle_options, show_answers, is_default) " +
                "VALUES ('Lain', false, false, false, 'Never', true)",
                "ux_rule_presets_one_system_default"
            ),
            (
                "INSERT INTO plans (code, name, includes_all_exams, includes_authoring, includes_classes, max_own_questions, " +
                "max_own_exams, max_import_rows, is_active) VALUES ('Teacher', 'Teacher', false, true, true, 0, 0, 0, true); " +
                "UPDATE subscriptions SET plan_id = (SELECT plan_id FROM plans WHERE code = 'Teacher')",
                "fk_subscriptions_plan_prices_plan_price_id_plan_id"
            ),
        ];

        var problems = new List<string>();
        await using var db = _database.CreateContext();
        foreach (var (sql, constraint) in cases)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                problems.Add($"accepted: {sql}");
            }
            catch (Npgsql.PostgresException ex) when (ex.ConstraintName != constraint)
            {
                problems.Add($"{sql} broke {ex.ConstraintName ?? ex.SqlState}, expected {constraint}");
            }
            catch (Npgsql.PostgresException)
            {
                // Rejected by the expected constraint.
            }
        }

        Assert.Empty(problems);
    }

    private async Task SeedOneOfEachAsync(Guid userId)
    {
        await using (var db = _database.CreateContext())
        {
            var role = new Role(RoleNames.Learner) { NormalizedName = "LEARNER" };
            var user = new User
            {
                Id = userId,
                UserName = "budi@example.com",
                Email = "budi@example.com",
                NormalizedEmail = "BUDI@EXAMPLE.COM",
                PhoneNumber = "+6281234567890",
                DisplayName = "Budi",
                WhatsAppOptIn = true,
                CreatedAt = Now,
            };
            var subject = new Subject { Name = "CPNS", Description = "Seleksi CPNS" };
            var topic = new Topic { Subject = subject, Name = "TWK" };
            var question = new Question
            {
                Topic = topic,
                QuestionType = QuestionType.SingleChoice,
                Text = "Ibu kota Indonesia?",
                Explanation = "Jakarta, sampai pemindahan ke IKN.",
                KeyFact = "Ibu kota: Jakarta",
                Difficulty = 2,
                SourceNote = "Kisi-kisi TWK 2026",
                Status = QuestionStatus.Active,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            question.Options.Add(new QuestionOption { Text = "Jakarta", IsCorrect = true, SortOrder = 1 });
            question.Options.Add(new QuestionOption { Text = "Bandung", SortOrder = 2 });
            var preset = new RulePreset { Name = "Standar", TimeLimitMinutes = 90, ShowAnswers = ShowAnswers.AfterEach, IsDefault = true };
            var exam = new Exam
            {
                Subject = subject,
                RulePreset = preset,
                Title = "Tryout CPNS 1",
                PriceIdr = 49_000,
                AccessDays = 30,
                OwnQuestionAllowance = 100,
                Status = ExamStatus.Published,
                ShareToken = "abc123",
                CreatedAt = Now,
            };
            exam.Questions.Add(new ExamQuestion { Question = question, SortOrder = 1, IsTrial = true });
            var plan = new Plan { Code = PlanCode.Learner, Name = "Learner", IncludesAllExams = true, MaxOwnQuestions = 1000, MaxOwnExams = 20, MaxImportRows = 500 };
            var price = new PlanPrice { Plan = plan, DurationMonths = 3, PriceIdr = 129_000 };

            db.Users.Add(user);
            db.Roles.Add(role);
            db.Exams.Add(exam);
            db.PlanPrices.Add(price);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
            db.Subscriptions.Add(new Subscription
            {
                UserId = userId, Plan = plan, PlanPrice = price, Status = SubscriptionStatus.Active,
                CurrentPeriodStart = Now, CurrentPeriodEnd = Now.AddMonths(3), CreatedAt = Now,
            });
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = userId, Exam = exam, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Active,
                PurchasedAt = Now, AccessExpiresAt = Now.AddDays(30),
            });
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = userId, Action = "Exam.Publish", EntityName = nameof(Exam),
                EntityId = exam.ExamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChangesJson = """{"status":"Published"}""", CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        }
    }

    private sealed record ColumnInfo(string Table, string Column, string Type, bool Nullable);
}
