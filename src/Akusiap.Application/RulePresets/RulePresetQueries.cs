using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.RulePresets;

public sealed record RulePresetDetail(
    long Id,
    string Name,
    int? TimeLimitMinutes,
    int? QuestionCount,
    bool HintsEnabled,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    int? MaxAttempts,
    int? PassMarkPercent,
    ShowAnswers ShowAnswers,
    bool IsDefault,
    int ExamCount);

/// <summary>System presets, default first, with how many exams use each.</summary>
public sealed class ListRulePresets(IAppDbContext db)
{
    public async Task<IReadOnlyList<RulePresetDetail>> HandleAsync(CancellationToken cancellationToken) =>
        // Sort before projecting: EF cannot translate ordering on a record built through its constructor.
        await RulePresetQueries.Details(
                db,
                db.RulePresets.Where(r => r.OwnerUserId == null).OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

public sealed class GetRulePreset(IAppDbContext db)
{
    public async Task<RulePresetDetail?> HandleAsync(long id, CancellationToken cancellationToken) =>
        await RulePresetQueries.Details(db, db.RulePresets.Where(r => r.Id == id && r.OwnerUserId == null))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}

internal static class RulePresetQueries
{
    public static IQueryable<RulePresetDetail> Details(IAppDbContext db, IQueryable<RulePreset> presets) =>
        presets.Select(r => new RulePresetDetail(
            r.Id,
            r.Name,
            r.TimeLimitMinutes,
            r.QuestionCount,
            r.HintsEnabled,
            r.ShuffleQuestions,
            r.ShuffleOptions,
            r.MaxAttempts,
            r.PassMarkPercent,
            r.ShowAnswers,
            r.IsDefault,
            db.Exams.Count(e => e.RulePresetId == r.Id)));
}
