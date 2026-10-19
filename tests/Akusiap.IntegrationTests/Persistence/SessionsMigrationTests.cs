using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Moderation;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>F-04: the session, memorization, moderation and notification tables of schema.sql.</summary>
public sealed class SessionsMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 19, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string[]> SchemaColumns = new()
    {
        ["attempts"] =
        [
            "id int8", "user_id uuid?", "guest_name text?", "guest_token uuid?", "exam_id int8?", "class_assignment_id int8?",
            "source text", "mode text", "status text", "rules_snapshot jsonb", "started_at timestamptz",
            "last_saved_at timestamptz", "submitted_at timestamptz?", "time_spent_seconds int4", "correct_count int4",
            "total_count int4", "score_percent numeric?", "passed bool?",
        ],
        ["attempt_items"] =
        [
            "id int8", "attempt_id int8", "question_id int8", "position int4", "option_order jsonb?", "is_answered bool",
            "is_correct bool", "is_flagged bool", "hint_used bool", "time_spent_seconds int4", "answered_at timestamptz?",
        ],
        ["attempt_item_options"] = ["attempt_item_id int8", "question_option_id int8"],
        ["review_cards"] =
        [
            "id int8", "user_id uuid", "question_id int8", "state text", "due_at timestamptz", "interval_days numeric",
            "ease_factor numeric", "repetitions int4", "lapses int4", "last_reviewed_at timestamptz?", "is_bookmarked bool",
            "note text?",
        ],
        ["review_logs"] =
        [
            "id int8", "review_card_id int8", "rating text", "interval_before_days numeric", "interval_after_days numeric",
            "reviewed_at timestamptz",
        ],
        ["content_reports"] =
        [
            "id int8", "reporter_user_id uuid?", "question_id int8?", "exam_id int8?", "reason text", "status text",
            "created_at timestamptz", "resolved_by_user_id uuid?", "resolved_at timestamptz?",
        ],
        ["notifications"] =
        [
            "id int8", "user_id uuid", "channel text", "template_code text", "language text", "payload jsonb", "status text",
            "external_message_id text?", "scheduled_at timestamptz", "sent_at timestamptz?", "error text?",
        ],
    };

    // numeric(p,s) columns in schema.sql.
    private static readonly string[] NumericPrecisions =
    [
        "attempts.score_percent 5,2", "review_cards.interval_days 8,2", "review_cards.ease_factor 4,2",
        "review_logs.interval_before_days 8,2", "review_logs.interval_after_days 8,2",
    ];

    private TemporaryDatabase _database = null!;

    public async Task InitializeAsync()
    {
        _database = await TemporaryDatabase.CreateMigratedAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Every_schema_column_exists_with_the_right_type_and_nullability()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindColumnProblemsAsync(db, SchemaColumns));
    }

    [Fact]
    public async Task Numeric_columns_keep_the_schema_precision()
    {
        await using var db = _database.CreateContext();
        var precisions = await db.Database
            .SqlQueryRaw<string>(
                "SELECT table_name || '.' || column_name || ' ' || numeric_precision || ',' || numeric_scale AS \"Value\" " +
                "FROM information_schema.columns WHERE table_schema = 'public' AND data_type = 'numeric'")
            .ToListAsync();

        Assert.Equivalent(NumericPrecisions, precisions);
    }

    [Fact]
    public async Task Unique_keys_exist()
    {
        (string Table, string Columns)[] expected =
        [
            ("attempt_items", "attempt_id, \"position\""),
            ("attempt_items", "attempt_id, question_id"),
            ("attempt_item_options", "attempt_item_id, question_option_id"),
            ("review_cards", "user_id, question_id"),
        ];
        await using var db = _database.CreateContext();

        var missing = new List<string>();
        foreach (var (table, columns) in expected)
        {
            if (!await SchemaAssertions.HasUniqueIndexAsync(db, table, columns))
            {
                missing.Add($"{table}({columns})");
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task One_of_each_entity_round_trips()
    {
        var seed = await SeedAsync();

        await using var db = _database.CreateContext();
        var attempt = await db.Attempts
            .Include(a => a.Items).ThenInclude(i => i.SelectedOptions)
            .SingleAsync(a => a.Id == seed.AttemptId);
        Assert.Equal(AttemptSource.Exam, attempt.Source);
        Assert.Equal(AttemptMode.Simulation, attempt.Mode);
        Assert.Equal(AttemptStatus.Submitted, attempt.Status);
        Assert.Equal(66.67m, attempt.ScorePercent);
        Assert.True(attempt.Passed);
        Assert.Contains("\"timeLimitMinutes\": 90", attempt.RulesSnapshot, StringComparison.Ordinal);
        var item = Assert.Single(attempt.Items);
        Assert.True(item.IsCorrect);
        Assert.Equal(seed.CorrectOptionId, Assert.Single(item.SelectedOptions).QuestionOptionId);

        var card = await db.ReviewCards.Include(c => c.Logs).SingleAsync();
        Assert.Equal(ReviewCardState.Learning, card.State);
        Assert.Equal(ReviewCard.DefaultEaseFactor, card.EaseFactor);
        Assert.Equal(1.5m, card.IntervalDays);
        Assert.Equal(ReviewRating.Hard, Assert.Single(card.Logs).Rating);

        var report = await db.ContentReports.SingleAsync();
        Assert.Equal(ContentReportStatus.Open, report.Status);
        Assert.Null(report.ReporterUserId);

        var notification = await db.Notifications.SingleAsync();
        Assert.Equal(NotificationTemplate.ReviewDue, notification.TemplateCode);
        Assert.Equal(NotificationStatus.Queued, notification.Status);
        Assert.Equal(Languages.Indonesian, notification.Language);
        Assert.Contains("\"dueCount\": 3", notification.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guests_can_take_trials_and_named_shared_exams_and_answers_go_with_their_attempt()
    {
        var seed = await SeedAsync();
        await using var db = _database.CreateContext();

        db.Attempts.Add(NewAttempt(userId: null, seed.ExamId, AttemptSource.Trial));
        var shared = NewAttempt(userId: null, seed.ExamId, AttemptSource.Shared);
        shared.GuestName = "Sari";
        db.Attempts.Add(shared);
        await db.SaveChangesAsync();

        db.Attempts.Remove(await db.Attempts.SingleAsync(a => a.Id == seed.AttemptId));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Attempts.CountAsync());
        Assert.Equal(0, await db.AttemptItems.CountAsync());
        Assert.Equal(0, await db.AttemptItemOptions.CountAsync());
    }

    [Fact]
    public async Task Database_rejects_rows_that_break_the_rules()
    {
        var seed = await SeedAsync();
        (string Sql, string Constraint)[] cases =
        [
            ("UPDATE attempts SET correct_count = total_count + 1", "ck_attempts_correct_within_total"),
            ("UPDATE attempts SET user_id = NULL", "ck_attempts_taker"),
            ("UPDATE attempts SET exam_id = NULL", "ck_attempts_exam_required"),
            ("UPDATE attempts SET submitted_at = NULL", "ck_attempts_submitted_at"),
            ("UPDATE attempts SET score_percent = 100.5", "ck_attempts_score_percent"),
            ("UPDATE attempts SET mode = 'Exam'", "ck_attempts_mode"),
            ("UPDATE attempt_items SET time_spent_seconds = -1", "ck_attempt_items_time_spent_seconds"),
            (
                "INSERT INTO attempt_items (attempt_id, question_id, position, is_answered, is_correct, is_flagged, hint_used, time_spent_seconds) " +
                $"SELECT attempt_id, {seed.OtherQuestionId}, position, false, false, false, false, 0 FROM attempt_items",
                "ix_attempt_items_attempt_id_position"
            ),
            (
                "INSERT INTO attempt_items (attempt_id, question_id, position, is_answered, is_correct, is_flagged, hint_used, time_spent_seconds) " +
                "SELECT attempt_id, question_id, position + 1, false, false, false, false, 0 FROM attempt_items",
                "ix_attempt_items_attempt_id_question_id"
            ),
            (
                "INSERT INTO review_cards (user_id, question_id, state, due_at, interval_days, ease_factor, repetitions, lapses, is_bookmarked) " +
                "SELECT user_id, question_id, 'New', now(), 0, 2.5, 0, 0, false FROM review_cards",
                "ix_review_cards_user_id_question_id"
            ),
            ("UPDATE review_cards SET ease_factor = 0", "ck_review_cards_ease_factor"),
            ("UPDATE review_cards SET interval_days = -1", "ck_review_cards_interval_days"),
            ("UPDATE review_cards SET state = 'Mastered'", "ck_review_cards_state"),
            ("UPDATE review_logs SET rating = 'Perfect'", "ck_review_logs_rating"),
            ("UPDATE content_reports SET question_id = NULL, exam_id = NULL", "ck_content_reports_target"),
            ("UPDATE notifications SET channel = 'WhatsApp'", "ck_notifications_channel"),
            ("UPDATE notifications SET template_code = 'PaymentInstructions'", "ck_notifications_template_code"),
            ("UPDATE notifications SET language = 'fr'", "ck_notifications_language"),
        ];
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindUnenforcedRulesAsync(db, cases));
    }

    private static Attempt NewAttempt(Guid? userId, long examId, AttemptSource source) => new()
    {
        UserId = userId,
        ExamId = examId,
        Source = source,
        Mode = AttemptMode.Practice,
        RulesSnapshot = """{"timeLimitMinutes": null}""",
        StartedAt = Now,
        LastSavedAt = Now,
    };

    private async Task<Seed> SeedAsync()
    {
        await using var db = _database.CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "rina@example.com",
            Email = "rina@example.com",
            DisplayName = "Rina",
            CreatedAt = Now,
        };
        var topic = new Topic { Subject = new Subject { Name = "UTBK" }, Name = "Penalaran Umum" };
        var question = NewQuestion(topic, "2 + 2 = ?");
        var correct = new QuestionOption { Text = "4", IsCorrect = true, SortOrder = 1 };
        question.Options.Add(correct);
        question.Options.Add(new QuestionOption { Text = "5", SortOrder = 2 });
        var otherQuestion = NewQuestion(topic, "3 + 3 = ?");
        var exam = new Exam
        {
            RulePreset = new RulePreset { Name = "Latihan standar", IsDefault = true },
            Title = "Tryout UTBK 1",
            IsFree = true,
            Status = ExamStatus.Published,
            CreatedAt = Now,
        };
        db.AddRange(user, question, otherQuestion, exam);
        await db.SaveChangesAsync();

        var attempt = new Attempt
        {
            UserId = user.Id,
            ExamId = exam.Id,
            Source = AttemptSource.Exam,
            Mode = AttemptMode.Simulation,
            Status = AttemptStatus.Submitted,
            RulesSnapshot = """{"timeLimitMinutes": 90}""",
            StartedAt = Now,
            LastSavedAt = Now.AddMinutes(30),
            SubmittedAt = Now.AddMinutes(30),
            TimeSpentSeconds = 1800,
            CorrectCount = 2,
            TotalCount = 3,
            ScorePercent = 66.67m,
            Passed = true,
        };
        var item = new AttemptItem
        {
            QuestionId = question.Id,
            Position = 1,
            OptionOrder = $"[{correct.Id}]",
            IsAnswered = true,
            IsCorrect = true,
            TimeSpentSeconds = 40,
            AnsweredAt = Now.AddMinutes(1),
        };
        item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = correct.Id });
        attempt.Items.Add(item);
        var card = new ReviewCard { UserId = user.Id, QuestionId = question.Id, State = ReviewCardState.Learning, DueAt = Now, IntervalDays = 1.5m };
        card.Logs.Add(new ReviewLog { Rating = ReviewRating.Hard, IntervalBeforeDays = 0, IntervalAfterDays = 1.5m, ReviewedAt = Now });
        db.AddRange(
            attempt,
            card,
            new ContentReport { QuestionId = question.Id, Reason = "Kunci jawaban salah", CreatedAt = Now },
            new Notification
            {
                UserId = user.Id,
                Channel = NotificationChannel.Email,
                TemplateCode = NotificationTemplate.ReviewDue,
                Payload = """{"dueCount": 3}""",
                ScheduledAt = Now,
            });
        await db.SaveChangesAsync();

        return new Seed(attempt.Id, exam.Id, correct.Id, otherQuestion.Id);
    }

    private static Question NewQuestion(Topic topic, string text) => new()
    {
        Topic = topic,
        QuestionType = QuestionType.SingleChoice,
        Text = text,
        Explanation = "Aritmetika dasar.",
        Status = QuestionStatus.Active,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private sealed record Seed(long AttemptId, long ExamId, long CorrectOptionId, long OtherQuestionId);
}
