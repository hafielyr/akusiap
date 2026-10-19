using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.Application.Sessions;

/// <summary>
/// The rules in force for one attempt, stored as <c>attempts.rules_snapshot</c> (D-09). <see cref="QuestionCount"/> is the
/// number of questions the attempt really has.
/// </summary>
public sealed record SessionRules(
    AttemptMode Mode,
    long RulePresetId,
    int? TimeLimitMinutes,
    int QuestionCount,
    bool HintsEnabled,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    int? MaxAttempts,
    int? PassMarkPercent,
    ShowAnswers ShowAnswers,
    IReadOnlyList<long> TopicIds);

/// <summary>What a learner asked to change in Practice. Null means "use the preset".</summary>
public sealed record RuleOverrides(
    int? QuestionCount = null,
    int? TimeLimitMinutes = null,
    bool? Untimed = null,
    bool? HintsEnabled = null,
    bool? ShuffleQuestions = null,
    bool? ShuffleOptions = null,
    IReadOnlyList<long>? TopicIds = null)
{
    public static RuleOverrides None { get; } = new();

    public bool IsEmpty => this == None || (QuestionCount is null && TimeLimitMinutes is null && Untimed is null && HintsEnabled is null
        && ShuffleQuestions is null && ShuffleOptions is null && (TopicIds is null || TopicIds.Count == 0));
}

/// <summary>
/// Builds <see cref="SessionRules"/> from the exam's preset and the learner's overrides (D-12, D-45). Simulation uses the
/// preset as is, without hints; Practice may lower the question count, choose topics, lengthen the time limit or go
/// untimed, turn hints off (or on when the preset allows them) and switch shuffling. Every out-of-bounds value is reported.
/// </summary>
public static class SessionRulesBuilder
{
    public const string ModeNotSupported = "Session.Rules.ModeNotSupported";
    public const string Locked = "Session.Rules.Locked";
    public const string CountRange = "Session.Rules.CountRange";
    public const string TimeTooShort = "Session.Rules.TimeTooShort";
    public const string TimeRange = "Session.Rules.TimeRange";
    public const string HintsOff = "Session.Rules.HintsOff";
    public const string NoQuestions = "Session.Rules.NoQuestions";
    public const int MaxTimeLimitMinutes = 600;

    /// <param name="available">Questions the viewer may get in the chosen topics.</param>
    public static Result<SessionRules> Build(RulePreset preset, AttemptMode mode, RuleOverrides overrides, int available)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(overrides);
        var topics = overrides.TopicIds?.Distinct().Order().ToList() ?? [];
        if (mode == AttemptMode.Flashcard)
        {
            return Result.Fail<SessionRules>(new Problem(ModeNotSupported));
        }

        if (mode == AttemptMode.Simulation)
        {
            if (!overrides.IsEmpty)
            {
                return Result.Fail<SessionRules>(new Problem(Locked));
            }

            if (available == 0)
            {
                return Result.Fail<SessionRules>(new Problem(NoQuestions));
            }

            return Result.Ok(Rules(preset, mode, preset.TimeLimitMinutes, Count(preset.QuestionCount, available), hints: false, preset.ShuffleQuestions, preset.ShuffleOptions, []));
        }

        var problems = new List<Problem>();
        if (available == 0)
        {
            problems.Add(new Problem(NoQuestions, nameof(RuleOverrides.TopicIds)));
        }

        if (overrides.QuestionCount is { } count && (count < 1 || count > available) && available > 0)
        {
            problems.Add(new Problem(CountRange, nameof(RuleOverrides.QuestionCount), available));
        }

        int? timeLimit = preset.TimeLimitMinutes;
        if (overrides.Untimed == true)
        {
            timeLimit = null;
        }
        else if (overrides.TimeLimitMinutes is { } minutes)
        {
            if (minutes is < 1 or > MaxTimeLimitMinutes)
            {
                problems.Add(new Problem(TimeRange, nameof(RuleOverrides.TimeLimitMinutes), MaxTimeLimitMinutes));
            }
            else if (preset.TimeLimitMinutes is { } floor && minutes < floor)
            {
                problems.Add(new Problem(TimeTooShort, nameof(RuleOverrides.TimeLimitMinutes), floor));
            }

            timeLimit = minutes;
        }

        if (overrides.HintsEnabled == true && !preset.HintsEnabled)
        {
            problems.Add(new Problem(HintsOff, nameof(RuleOverrides.HintsEnabled)));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<SessionRules>(problems);
        }

        return Result.Ok(Rules(
            preset,
            mode,
            timeLimit,
            overrides.QuestionCount ?? Count(preset.QuestionCount, available),
            overrides.HintsEnabled ?? preset.HintsEnabled,
            overrides.ShuffleQuestions ?? preset.ShuffleQuestions,
            overrides.ShuffleOptions ?? preset.ShuffleOptions,
            topics));
    }

    private static int Count(int? presetCount, int available) => presetCount is { } n ? Math.Min(n, available) : available;

    private static SessionRules Rules(
        RulePreset preset, AttemptMode mode, int? timeLimit, int count, bool hints, bool shuffleQuestions, bool shuffleOptions, IReadOnlyList<long> topics) =>
        new(mode, preset.Id, timeLimit, count, hints, shuffleQuestions, shuffleOptions, preset.MaxAttempts, preset.PassMarkPercent, preset.ShowAnswers, topics);
}
