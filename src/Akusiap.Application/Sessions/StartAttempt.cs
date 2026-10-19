using System.Text.Json;
using System.Text.Json.Serialization;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Catalog;
using Akusiap.Application.Common;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <param name="GuestToken">The guest's cookie token (required when nobody is signed in).</param>
/// <param name="GuestName">Shared-exam takers who are not signed in.</param>
public sealed record StartAttemptRequest(
    long ExamId,
    AttemptMode Mode,
    RuleOverrides? Overrides = null,
    string? ShareToken = null,
    Guid? GuestToken = null,
    string? GuestName = null);

public sealed record StartedAttempt(long AttemptId, AttemptSource Source, int QuestionCount);

/// <summary>
/// Starts an attempt (SES-01, stories S-01, S-03, S-08, G-02): checks the entitlement, builds the rules (D-12, D-45),
/// picks questions from <see cref="ExamQuestionScope"/> (trial questions only for a trial), shuffles questions and options
/// as the rules say, and stores the attempt with its items and rules snapshot.
/// </summary>
public sealed class StartAttempt(
    IAppDbContext db,
    IEntitlementService entitlements,
    ExamQuestionScope questionScope,
    ICurrentUser currentUser,
    IClock clock,
    IRandomSource random)
{
    public const string NoAccess = "Session.NoAccess";
    public const string GuestNameRequired = "Session.GuestNameRequired";
    public const string MaxAttemptsReached = "Session.MaxAttemptsReached";
    public const string NothingWrong = "Session.Retake.NothingWrong";
    public const string MixEmpty = "Mix.Empty";

    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async Task<Result<StartedAttempt>> HandleAsync(StartAttemptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = currentUser.UserId;
        if (userId is null && request.GuestToken is null)
        {
            throw new InvalidOperationException("Guests need a guest token to start an attempt.");
        }

        var access = await entitlements.GetAsync(request.ExamId, request.ShareToken, cancellationToken).ConfigureAwait(false);
        if (access is null || !ExamCtas.IsVisible(access.Decision))
        {
            return Result.Fail<StartedAttempt>(Problem.NotFound);
        }

        var decision = access.Decision;
        if (!decision.CanStart)
        {
            return Result.Fail<StartedAttempt>(new Problem(NoAccess));
        }

        var guestName = request.GuestName?.Trim();
        if (userId is null && decision.RequiresGuestName && string.IsNullOrEmpty(guestName))
        {
            return Result.Fail<StartedAttempt>(new Problem(GuestNameRequired, nameof(StartAttemptRequest.GuestName)));
        }

        var presetId = await db.Exams.Where(e => e.Id == request.ExamId).Select(e => e.RulePresetId).SingleAsync(cancellationToken).ConfigureAwait(false);
        var preset = await db.RulePresets.AsNoTracking().SingleAsync(p => p.Id == presetId, cancellationToken).ConfigureAwait(false);
        var candidates = await CandidatesAsync(request.ExamId, decision.Scope, cancellationToken).ConfigureAwait(false);
        var overrides = request.Overrides ?? RuleOverrides.None;
        var topics = overrides.TopicIds ?? [];
        var built = SessionRulesBuilder.Build(preset, request.Mode, overrides, QuestionSelection.CountAvailable(candidates, [.. topics]));
        if (!built.Succeeded)
        {
            return Result.Fail<StartedAttempt>(built.Problems);
        }

        var rules = built.Value!;
        if (rules.Mode == AttemptMode.Simulation && rules.MaxAttempts is { } max)
        {
            var used = await db.Attempts
                .CountAsync(
                    a => a.ExamId == request.ExamId && a.Mode == AttemptMode.Simulation
                        && (userId != null ? a.UserId == userId : a.UserId == null && a.GuestToken == request.GuestToken),
                    cancellationToken)
                .ConfigureAwait(false);
            if (used >= max)
            {
                return Result.Fail<StartedAttempt>(new Problem(MaxAttemptsReached, null, max));
            }
        }

        var selected = QuestionSelection.Select(candidates, [.. rules.TopicIds], rules.QuestionCount, rules.ShuffleQuestions, rules.ShuffleOptions, random.Random);
        var source = decision.Scope == QuestionScope.TrialOnly ? AttemptSource.Trial
            : decision.Level == AccessLevel.Shared ? AttemptSource.Shared
            : AttemptSource.Exam;
        var now = clock.UtcNow;
        var attempt = new Attempt
        {
            UserId = userId,
            GuestToken = userId is null ? request.GuestToken : null,
            GuestName = userId is null && source == AttemptSource.Shared ? guestName : null,
            ExamId = request.ExamId,
            Source = source,
            Mode = rules.Mode,
            RulesSnapshot = JsonSerializer.Serialize(rules, Json),
            StartedAt = now,
            LastSavedAt = now,
            TotalCount = selected.Count,
        };
        foreach (var item in selected)
        {
            attempt.Items.Add(new AttemptItem
            {
                QuestionId = item.QuestionId,
                Position = item.Position,
                OptionOrder = JsonSerializer.Serialize(item.OptionOrder, Json),
            });
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new StartedAttempt(attempt.Id, source, selected.Count));
    }

    /// <summary>
    /// "Ulangi yang salah" (SES-09, story R-04, D-80): a new Practice attempt, Source WrongOnly, with exactly the questions
    /// answered wrong (or left blank) in a finished attempt of the viewer. The entitlement is checked again and the
    /// questions are limited to what it allows now, so a trial retake stays within the trial set. Untimed, every wrong
    /// question, no pass mark or attempt limit; hints, shuffling and answer display follow the original rules.
    /// </summary>
    public async Task<Result<StartedAttempt>> RetakeWrongAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new { a.UserId, a.GuestToken, a.ExamId, a.Status, a.RulesSnapshot })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (source is null || source.ExamId is not { } examId || source.Status == AttemptStatus.InProgress
            || !AttemptAccess.IsOwner(currentUser, source.UserId, source.GuestToken, guestToken))
        {
            return Result.Fail<StartedAttempt>(Problem.NotFound);
        }

        var wrong = await db.AttemptItems
            .Where(i => i.AttemptId == attemptId && !i.IsCorrect)
            .Select(i => i.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (wrong.Count == 0)
        {
            return Result.Fail<StartedAttempt>(new Problem(NothingWrong));
        }

        var access = await entitlements.GetAsync(examId, shareToken: null, cancellationToken).ConfigureAwait(false);
        if (access is null || !access.Decision.CanStart)
        {
            return Result.Fail<StartedAttempt>(new Problem(NoAccess));
        }

        var candidates = (await CandidatesAsync(examId, access.Decision.Scope, cancellationToken).ConfigureAwait(false))
            .Where(c => wrong.Contains(c.QuestionId))
            .ToList();
        if (candidates.Count == 0)
        {
            return Result.Fail<StartedAttempt>(new Problem(NoAccess));
        }

        var original = JsonSerializer.Deserialize<SessionRules>(source.RulesSnapshot, Json)!;
        var rules = original with
        {
            Mode = AttemptMode.Practice,
            TimeLimitMinutes = null,
            QuestionCount = candidates.Count,
            MaxAttempts = null,
            PassMarkPercent = null,
            TopicIds = [],
        };
        var selected = QuestionSelection.Select(candidates, [], rules.QuestionCount, rules.ShuffleQuestions, rules.ShuffleOptions, random.Random);
        var now = clock.UtcNow;
        var attempt = new Attempt
        {
            UserId = userId,
            GuestToken = userId is null ? source.GuestToken : null,
            ExamId = examId,
            Source = AttemptSource.WrongOnly,
            Mode = rules.Mode,
            RulesSnapshot = JsonSerializer.Serialize(rules, Json),
            StartedAt = now,
            LastSavedAt = now,
            TotalCount = selected.Count,
        };
        foreach (var item in selected)
        {
            attempt.Items.Add(new AttemptItem
            {
                QuestionId = item.QuestionId,
                Position = item.Position,
                OptionOrder = JsonSerializer.Serialize(item.OptionOrder, Json),
            });
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new StartedAttempt(attempt.Id, AttemptSource.WrongOnly, selected.Count));
    }
    /// <summary>
    /// Starts a private mixed set (OWN-06, D-84) from questions the caller already checked: Practice, untimed, hints on,
    /// shuffled, answers shown after each check. Source CustomMix and no exam, so it can never be shared.
    /// </summary>
    public async Task<Result<StartedAttempt>> StartCustomMixAsync(IReadOnlyList<long> questionIds, int questionCount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(questionIds);
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Mixed practice needs a signed-in user.");
        var candidates = await CandidatesForAsync(questionIds, cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return Result.Fail<StartedAttempt>(new Problem(MixEmpty));
        }

        var preset = new RulePreset { Name = "Latihan campuran", HintsEnabled = true, ShuffleQuestions = true, ShuffleOptions = true, ShowAnswers = ShowAnswers.AfterEach };
        var built = SessionRulesBuilder.Build(preset, AttemptMode.Practice, new RuleOverrides(QuestionCount: Math.Min(questionCount, candidates.Count), Untimed: true), candidates.Count);
        if (!built.Succeeded)
        {
            return Result.Fail<StartedAttempt>(built.Problems);
        }

        var rules = built.Value!;
        var selected = QuestionSelection.Select(candidates, [], rules.QuestionCount, rules.ShuffleQuestions, rules.ShuffleOptions, random.Random);
        var now = clock.UtcNow;
        var attempt = new Attempt
        {
            UserId = userId,
            ExamId = null,
            Source = AttemptSource.CustomMix,
            Mode = rules.Mode,
            RulesSnapshot = JsonSerializer.Serialize(rules, Json),
            StartedAt = now,
            LastSavedAt = now,
            TotalCount = selected.Count,
        };
        foreach (var item in selected)
        {
            attempt.Items.Add(new AttemptItem
            {
                QuestionId = item.QuestionId,
                Position = item.Position,
                OptionOrder = JsonSerializer.Serialize(item.OptionOrder, Json),
            });
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new StartedAttempt(attempt.Id, AttemptSource.CustomMix, selected.Count));
    }

    /// <summary>The questions the decision allows, in exam order, with topic, type and option ids.</summary>
    private async Task<IReadOnlyList<CandidateQuestion>> CandidatesAsync(long examId, QuestionScope scope, CancellationToken cancellationToken)
    {
        var ids = await questionScope.GetQuestionIdsAsync(examId, scope, cancellationToken).ConfigureAwait(false);
        return await CandidatesForAsync(ids, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The given questions, in the given order, with topic, type and option ids (missing ones are skipped).</summary>
    private async Task<IReadOnlyList<CandidateQuestion>> CandidatesForAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var rows = await db.Questions
            .Where(q => ids.Contains(q.Id))
            .Select(q => new
            {
                q.Id,
                q.TopicId,
                q.QuestionType,
                Options = q.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => o.Id).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byId = rows.ToDictionary(r => r.Id);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(r => new CandidateQuestion(r.Id, r.TopicId, r.QuestionType, r.Options))];
    }
}
