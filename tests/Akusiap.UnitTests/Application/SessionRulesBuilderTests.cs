using Akusiap.Application.Sessions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Application;

/// <summary>SES-01 and D-12: the rules an attempt runs under, from the exam's preset and the learner's overrides.</summary>
public sealed class SessionRulesBuilderTests
{
    private static RulePreset Preset(int? time = 60, int? count = 20, bool hints = true) => new()
    {
        Id = 9,
        Name = "Tryout",
        TimeLimitMinutes = time,
        QuestionCount = count,
        HintsEnabled = hints,
        ShuffleQuestions = true,
        ShuffleOptions = true,
        MaxAttempts = 3,
        PassMarkPercent = 65,
        ShowAnswers = ShowAnswers.AfterSubmit,
    };

    [Fact]
    public void Simulation_uses_the_preset_as_is_without_hints()
    {
        var rules = SessionRulesBuilder.Build(Preset(), AttemptMode.Simulation, RuleOverrides.None, available: 50).Value!;

        Assert.Equal(
            (AttemptMode.Simulation, 9L, (int?)60, 20, false, true, true, (int?)3, (int?)65, ShowAnswers.AfterSubmit),
            (rules.Mode, rules.RulePresetId, rules.TimeLimitMinutes, rules.QuestionCount, rules.HintsEnabled, rules.ShuffleQuestions, rules.ShuffleOptions, rules.MaxAttempts, rules.PassMarkPercent, rules.ShowAnswers));
        Assert.Empty(rules.TopicIds);
    }

    [Fact]
    public void Simulation_rejects_any_override()
    {
        var result = SessionRulesBuilder.Build(Preset(), AttemptMode.Simulation, new RuleOverrides(QuestionCount: 5), 50);

        Assert.Equal([SessionRulesBuilder.Locked], result.Problems.Select(p => p.Code));
    }

    [Fact]
    public void Preset_count_is_capped_by_the_questions_available()
    {
        Assert.Equal(12, SessionRulesBuilder.Build(Preset(count: 20), AttemptMode.Practice, RuleOverrides.None, 12).Value!.QuestionCount);
        Assert.Equal(12, SessionRulesBuilder.Build(Preset(count: null), AttemptMode.Practice, RuleOverrides.None, 12).Value!.QuestionCount);
    }

    [Fact]
    public void Practice_accepts_overrides_within_bounds()
    {
        var overrides = new RuleOverrides(QuestionCount: 30, TimeLimitMinutes: 90, HintsEnabled: false, ShuffleQuestions: false, ShuffleOptions: false, TopicIds: [5, 3, 5]);

        var rules = SessionRulesBuilder.Build(Preset(), AttemptMode.Practice, overrides, available: 40).Value!;

        Assert.Equal((30, 90, false, false, false), (rules.QuestionCount, rules.TimeLimitMinutes, rules.HintsEnabled, rules.ShuffleQuestions, rules.ShuffleOptions));
        Assert.Equal([3L, 5], rules.TopicIds);
    }

    [Fact]
    public void Practice_may_go_untimed()
    {
        Assert.Null(SessionRulesBuilder.Build(Preset(), AttemptMode.Practice, new RuleOverrides(Untimed: true), 40).Value!.TimeLimitMinutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(41)]
    public void Count_must_be_1_to_the_questions_available(int count)
    {
        var result = SessionRulesBuilder.Build(Preset(), AttemptMode.Practice, new RuleOverrides(QuestionCount: count), 40);

        var problem = Assert.Single(result.Problems);
        Assert.Equal((SessionRulesBuilder.CountRange, "QuestionCount", 40), (problem.Code, problem.Field, (int)problem.Args[0]));
    }

    [Fact]
    public void Time_limit_cannot_be_shorter_than_the_preset_or_out_of_range()
    {
        var shorter = SessionRulesBuilder.Build(Preset(time: 60), AttemptMode.Practice, new RuleOverrides(TimeLimitMinutes: 30), 40);
        var tooLong = SessionRulesBuilder.Build(Preset(time: null), AttemptMode.Practice, new RuleOverrides(TimeLimitMinutes: 601), 40);
        var untimedPreset = SessionRulesBuilder.Build(Preset(time: null), AttemptMode.Practice, new RuleOverrides(TimeLimitMinutes: 15), 40);

        Assert.Equal([SessionRulesBuilder.TimeTooShort], shorter.Problems.Select(p => p.Code));
        Assert.Equal([SessionRulesBuilder.TimeRange], tooLong.Problems.Select(p => p.Code));
        Assert.Equal(15, untimedPreset.Value!.TimeLimitMinutes);
    }

    [Fact]
    public void Hints_cannot_be_turned_on_when_the_preset_disables_them()
    {
        var result = SessionRulesBuilder.Build(Preset(hints: false), AttemptMode.Practice, new RuleOverrides(HintsEnabled: true), 40);

        Assert.Equal([SessionRulesBuilder.HintsOff], result.Problems.Select(p => p.Code));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var result = SessionRulesBuilder.Build(Preset(hints: false), AttemptMode.Practice, new RuleOverrides(QuestionCount: 99, TimeLimitMinutes: 5, HintsEnabled: true), 40);

        Assert.Equal(3, result.Problems.Count);
    }

    [Fact]
    public void No_questions_in_the_chosen_topics_is_a_problem()
    {
        Assert.Equal([SessionRulesBuilder.NoQuestions], SessionRulesBuilder.Build(Preset(), AttemptMode.Practice, new RuleOverrides(TopicIds: [1]), 0).Problems.Select(p => p.Code));
    }

    [Fact]
    public void Flashcard_mode_is_not_started_here()
    {
        Assert.Equal([SessionRulesBuilder.ModeNotSupported], SessionRulesBuilder.Build(Preset(), AttemptMode.Flashcard, RuleOverrides.None, 10).Problems.Select(p => p.Code));
    }

    [Fact]
    public void The_cat_screen_applies_only_to_simulations()
    {
        var preset = Preset();
        preset.CatMode = true;

        var simulation = SessionRulesBuilder.Build(preset, AttemptMode.Simulation, RuleOverrides.None, 50).Value!;
        var practice = SessionRulesBuilder.Build(preset, AttemptMode.Practice, RuleOverrides.None, 50).Value!;

        Assert.True(simulation.CatMode);
        Assert.False(practice.CatMode);
        Assert.False(SessionRulesBuilder.Build(Preset(), AttemptMode.Simulation, RuleOverrides.None, 50).Value!.CatMode);
    }
}
