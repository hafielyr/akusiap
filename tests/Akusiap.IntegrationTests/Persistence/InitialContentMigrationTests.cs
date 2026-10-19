using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>F-03: the migration builds the content and access tables of schema.sql on an empty database.</summary>
public sealed class InitialContentMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc);

    // schema.sql columns per table: "column type", "?" marks nullable. Types are PostgreSQL udt names.
    // roles.id and user_roles.role_id are uuid instead of bigint because Identity needs one key type (D-20).
    private static readonly Dictionary<string, string[]> SchemaColumns = new()
    {
        ["users"] =
        [
            "id uuid", "email text", "email_confirmed bool", "phone_number text?", "password_hash text?",
            "display_name text", "preferred_language text", "is_active bool", "deactivated_at timestamptz?",
            "created_at timestamptz",
        ],
        ["roles"] = ["id uuid", "name text"],
        ["user_roles"] = ["user_id uuid", "role_id uuid"],
        ["plans"] =
        [
            "id int8", "code text", "name text", "includes_all_exams bool", "includes_authoring bool",
            "includes_classes bool", "max_own_questions int4?", "max_own_exams int4?", "max_import_rows int4?", "is_active bool",
        ],
        ["plan_prices"] = ["id int8", "plan_id int8", "duration_months int4", "price_idr int8", "is_active bool"],
        ["subjects"] = ["id int8", "name text", "description text?"],
        ["topics"] = ["id int8", "subject_id int8", "name text", "description text?"],
        ["rule_presets"] =
        [
            "id int8", "owner_user_id uuid?", "name text", "time_limit_minutes int4?", "question_count int4?",
            "hints_enabled bool", "shuffle_questions bool", "shuffle_options bool", "max_attempts int4?",
            "pass_mark_percent int4?", "show_answers text", "is_default bool",
        ],
        ["exams"] =
        [
            "id int8", "owner_user_id uuid?", "owner_kind text", "subject_id int8?", "rule_preset_id int8", "title text",
            "description text?", "is_free bool", "price_idr int8?", "access_days int4?", "own_question_allowance int4",
            "status text", "is_shared bool", "share_token text?", "created_at timestamptz",
        ],
        ["questions"] =
        [
            "id int8", "owner_user_id uuid?", "topic_id int8", "question_type text", "text text", "explanation text",
            "key_fact text?", "hint text?", "difficulty int2?", "source_note text?", "variant_group_id uuid?",
            "status text", "created_at timestamptz", "updated_at timestamptz",
        ],
        ["question_options"] = ["id int8", "question_id int8", "text text", "is_correct bool", "sort_order int4"],
        ["exam_questions"] = ["exam_id int8", "question_id int8", "sort_order int4", "is_trial bool"],
        ["subscriptions"] =
        [
            "id int8", "user_id uuid", "plan_id int8", "plan_price_id int8", "status text",
            "current_period_start timestamptz?", "current_period_end timestamptz?", "renewal_mode text",
            "saved_payment_method_id int8?", "grace_until timestamptz?", "reminder_7d_sent_at timestamptz?",
            "reminder_1d_sent_at timestamptz?", "gateway_subscription_id text?", "created_at timestamptz",
        ],
        ["exam_purchases"] =
        [
            "id int8", "user_id uuid", "exam_id int8", "price_paid_idr int8", "status text",
            "purchased_at timestamptz?", "access_expires_at timestamptz?",
        ],
        ["audit_logs"] =
        [
            "id int8", "actor_user_id uuid", "action text", "entity_name text", "entity_id text", "changes jsonb?",
            "created_at timestamptz",
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
    public async Task Every_schema_column_exists_with_the_right_type_and_nullability()
    {
        await using var db = _database.CreateContext();
        var actual = await db.Database
            .SqlQueryRaw<ColumnInfo>(
                "SELECT table_name AS \"Table\", column_name AS \"Column\", udt_name AS \"Type\", is_nullable = 'YES' AS \"Nullable\" " +
                "FROM information_schema.columns WHERE table_schema = 'public'")
            .ToListAsync();
        var columns = actual.ToDictionary(c => (c.Table, c.Column));

        var problems = new List<string>();
        foreach (var (table, expectedColumns) in SchemaColumns)
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
    public async Task Unique_indexes_exist()
    {
        await using var db = _database.CreateContext();
        var uniqueIndexes = await db.Database
            .SqlQueryRaw<string>(
                "SELECT tablename || ': ' || indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE%'")
            .ToListAsync();
        bool Has(string table, string columns) =>
            uniqueIndexes.Any(i => i.StartsWith(table + ":", StringComparison.Ordinal) && i.Contains($"({columns})", StringComparison.Ordinal));

        Assert.True(Has("users", "lower(email)"), "users(lower(email))");
        Assert.True(Has("exams", "share_token"), "exams(share_token)");
        Assert.True(Has("exam_questions", "exam_id, question_id"), "exam_questions(exam_id, question_id)");
        Assert.True(Has("plans", "code"), "plans(code)");
        Assert.True(Has("subjects", "name"), "subjects(name)");
        Assert.True(Has("topics", "subject_id, name"), "topics(subject_id, name)");
        Assert.True(Has("plan_prices", "plan_id, duration_months"), "plan_prices(plan_id, duration_months)");
        Assert.True(Has("subscriptions", "user_id, plan_id"), "subscriptions(user_id, plan_id)");
        Assert.True(Has("exam_purchases", "user_id, exam_id"), "exam_purchases(user_id, exam_id)");
    }

    [Fact]
    public async Task One_of_each_entity_round_trips()
    {
        var userId = Guid.NewGuid();
        await SeedOneOfEachAsync(userId);

        await using var db = _database.CreateContext();
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
        Assert.Null(exam.Description);
        Assert.Equal("CPNS", exam.Subject!.Name);
        Assert.Equal(ShowAnswers.AfterEach, exam.RulePreset!.ShowAnswers);
        Assert.True(exam.RulePreset.ShuffleOptions);
        var examQuestion = Assert.Single(exam.Questions);
        Assert.True(examQuestion.IsTrial);
        Assert.Equal("TWK", examQuestion.Question!.Topic!.Name);
        Assert.Equal((short)2, examQuestion.Question.Difficulty);
        Assert.Equal(2, examQuestion.Question.Options.Count);
        Assert.Single(examQuestion.Question.Options, o => o.IsCorrect);

        var subscription = await db.Subscriptions.Include(s => s.Plan).Include(s => s.PlanPrice).SingleAsync();
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(RenewalMode.Manual, subscription.RenewalMode);
        Assert.Equal(PlanCode.Learner, subscription.Plan!.Code);
        Assert.Null(subscription.Plan.MaxOwnQuestions);
        Assert.Equal(129_000, subscription.PlanPrice!.PriceIdr);
        Assert.Equal(Now.AddDays(-1), subscription.Reminder7dSentAt);

        var purchase = await db.ExamPurchases.SingleAsync();
        Assert.Equal(ExamPurchaseStatus.Active, purchase.Status);
        Assert.Equal(Now.AddDays(30), purchase.AccessExpiresAt);

        var audit = await db.AuditLogs.SingleAsync();
        Assert.Contains("Published", audit.Changes, StringComparison.Ordinal);

        // Enums are stored as their names.
        var storedStatus = await db.Database.SqlQueryRaw<string>("SELECT status AS \"Value\" FROM subscriptions").SingleAsync();
        Assert.Equal("Active", storedStatus);
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
            ("UPDATE subscriptions SET status = 'PendingPayment'", "ck_subscriptions_status"),
            ("UPDATE subscriptions SET renewal_mode = 'AutoDebit'", "ck_subscriptions_auto_debit_method"),
            ("UPDATE subscriptions SET current_period_end = current_period_start", "ck_subscriptions_active_period"),
            ("UPDATE exam_purchases SET access_expires_at = NULL", "ck_exam_purchases_active_paid"),
            ("INSERT INTO subjects (name) VALUES ('CPNS')", "ix_subjects_name"),
            (
                "INSERT INTO users (id, email, email_confirmed, display_name, preferred_language, is_active, created_at, " +
                "phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count) " +
                "VALUES (gen_random_uuid(), 'BUDI@example.com', false, 'Budi 2', 'id', true, now(), false, false, false, 0)",
                "ux_users_email"
            ),
            (
                "INSERT INTO rule_presets (name, hints_enabled, shuffle_questions, shuffle_options, show_answers, is_default) " +
                "VALUES ('Lain', false, false, false, 'Never', true)",
                "ux_rule_presets_one_system_default"
            ),
            (
                "INSERT INTO subscriptions (user_id, plan_id, plan_price_id, status, renewal_mode, created_at) " +
                "SELECT user_id, plan_id, plan_price_id, 'AwaitingPayment', 'Manual', now() FROM subscriptions",
                "ux_subscriptions_one_per_plan"
            ),
            (
                "INSERT INTO exam_purchases (user_id, exam_id, price_paid_idr, status) " +
                "SELECT user_id, exam_id, 49000, 'AwaitingPayment' FROM exam_purchases",
                "ux_exam_purchases_one_per_exam"
            ),
            (
                "INSERT INTO plans (code, name, includes_all_exams, includes_authoring, includes_classes, is_active) " +
                "VALUES ('Teacher', 'Paket Pengajar', false, true, true, true); " +
                "UPDATE subscriptions SET plan_id = (SELECT id FROM plans WHERE code = 'Teacher')",
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
            catch (PostgresException ex) when (ex.ConstraintName != constraint)
            {
                problems.Add($"{sql} broke {ex.ConstraintName ?? ex.SqlState}, expected {constraint}");
            }
            catch (PostgresException)
            {
                // Rejected by the expected constraint.
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public async Task Abandoned_checkouts_do_not_block_a_new_one()
    {
        await SeedOneOfEachAsync(Guid.NewGuid());
        await using var db = _database.CreateContext();

        await db.Database.ExecuteSqlRawAsync("UPDATE exam_purchases SET status = 'Abandoned'");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO exam_purchases (user_id, exam_id, price_paid_idr, status) " +
            "SELECT user_id, exam_id, 49000, 'AwaitingPayment' FROM exam_purchases");

        Assert.Equal(2, await db.ExamPurchases.CountAsync());
    }

    private async Task SeedOneOfEachAsync(Guid userId)
    {
        await using var db = _database.CreateContext();
        var role = new Role(RoleNames.Learner) { NormalizedName = "LEARNER" };
        var user = new User
        {
            Id = userId,
            UserName = "budi@example.com",
            Email = "budi@example.com",
            NormalizedEmail = "BUDI@EXAMPLE.COM",
            PhoneNumber = "+6281234567890",
            DisplayName = "Budi",
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
        var preset = new RulePreset { Name = "Latihan standar", ShowAnswers = ShowAnswers.AfterEach, IsDefault = true };
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
        var plan = new Plan { Code = PlanCode.Learner, Name = "Paket Pelajar", IncludesAllExams = true, IncludesAuthoring = true };
        var price = new PlanPrice { Plan = plan, DurationMonths = 3, PriceIdr = 129_000 };

        db.Users.Add(user);
        db.Roles.Add(role);
        db.Exams.Add(exam);
        db.PlanPrices.Add(price);
        await db.SaveChangesAsync();

        db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = role.Id });
        db.Subscriptions.Add(new Subscription
        {
            UserId = userId,
            Plan = plan,
            PlanPrice = price,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = Now,
            CurrentPeriodEnd = Now.AddMonths(3),
            Reminder7dSentAt = Now.AddDays(-1),
            CreatedAt = Now,
        });
        db.ExamPurchases.Add(new ExamPurchase
        {
            UserId = userId,
            Exam = exam,
            PricePaidIdr = 49_000,
            Status = ExamPurchaseStatus.Active,
            PurchasedAt = Now,
            AccessExpiresAt = Now.AddDays(30),
        });
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = userId,
            Action = "Exam.Publish",
            EntityName = nameof(Exam),
            EntityId = exam.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Changes = """{"status":"Published"}""",
            CreatedAt = Now,
        });
        await db.SaveChangesAsync();
    }

    private sealed record ColumnInfo(string Table, string Column, string Type, bool Nullable);
}
