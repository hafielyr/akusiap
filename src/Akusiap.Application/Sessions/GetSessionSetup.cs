using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Catalog;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>A topic the learner can pick, with how many of its questions they may get.</summary>
public sealed record SetupTopic(long Id, string Name, int QuestionCount);

/// <summary>The exam's rules as the setup page shows them.</summary>
public sealed record SetupPreset(int? TimeLimitMinutes, int? QuestionCount, bool HintsEnabled, bool ShuffleQuestions, bool ShuffleOptions, int? MaxAttempts, int? PassMarkPercent);

/// <param name="AvailableCount">Questions in the viewer's scope (the trial set for a trial).</param>
/// <param name="SimulationAttemptsLeft">Null when simulations are unlimited.</param>
public sealed record SessionSetup(
    long ExamId,
    string Title,
    AccessDecision Decision,
    SetupPreset Preset,
    IReadOnlyList<SetupTopic> Topics,
    int AvailableCount,
    int? SimulationAttemptsLeft)
{
    public bool IsTrial => Decision.Scope == QuestionScope.TrialOnly;

    /// <summary>Practice starts with the preset's count, capped at what is available.</summary>
    public int DefaultQuestionCount => Preset.QuestionCount is { } n ? Math.Min(n, AvailableCount) : AvailableCount;
}

/// <summary>
/// What the session setup page needs (SES-02): the exam, the viewer's access decision, the preset, the topics in the
/// viewer's question scope and how many simulations are left. Null when the exam is missing or not visible.
/// </summary>
public sealed class GetSessionSetup(IAppDbContext db, IEntitlementService entitlements, ExamQuestionScope questionScope, ICurrentUser currentUser)
{
    public async Task<SessionSetup?> HandleAsync(long examId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var access = await entitlements.GetAsync(examId, shareToken: null, cancellationToken).ConfigureAwait(false);
        if (access is null || !ExamCtas.IsVisible(access.Decision))
        {
            return null;
        }

        var exam = await db.Exams
            .Where(e => e.Id == examId)
            .Select(e => new
            {
                e.Title,
                Preset = new SetupPreset(
                    e.RulePreset!.TimeLimitMinutes,
                    e.RulePreset.QuestionCount,
                    e.RulePreset.HintsEnabled,
                    e.RulePreset.ShuffleQuestions,
                    e.RulePreset.ShuffleOptions,
                    e.RulePreset.MaxAttempts,
                    e.RulePreset.PassMarkPercent),
            })
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = await questionScope.GetQuestionIdsAsync(examId, access.Decision.Scope, cancellationToken).ConfigureAwait(false);
        var topics = await db.Questions
            .Where(q => ids.Contains(q.Id))
            .GroupBy(q => new { q.TopicId, q.Topic!.Name })
            .Select(g => new SetupTopic(g.Key.TopicId, g.Key.Name, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int? attemptsLeft = null;
        if (exam.Preset.MaxAttempts is { } max)
        {
            var userId = currentUser.UserId;
            var used = userId is null && guestToken is null
                ? 0
                : await db.Attempts.CountAsync(
                    a => a.ExamId == examId && a.Mode == AttemptMode.Simulation
                        && (userId != null ? a.UserId == userId : a.UserId == null && a.GuestToken == guestToken),
                    cancellationToken).ConfigureAwait(false);
            attemptsLeft = Math.Max(0, max - used);
        }

        return new SessionSetup(
            examId,
            exam.Title,
            access.Decision,
            exam.Preset,
            [.. topics.OrderBy(t => t.Name, StringComparer.CurrentCulture)],
            ids.Count,
            attemptsLeft);
    }
}
