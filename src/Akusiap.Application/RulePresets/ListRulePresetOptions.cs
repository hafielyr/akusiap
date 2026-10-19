using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.RulePresets;

public sealed record RulePresetOption(long Id, string Name, bool IsDefault);

/// <summary>System rule presets (no owner) for dropdowns, the default first. ADM-T5 adds editing.</summary>
public sealed class ListRulePresetOptions(IAppDbContext db)
{
    public async Task<IReadOnlyList<RulePresetOption>> HandleAsync(CancellationToken cancellationToken) =>
        await db.RulePresets
            .Where(r => r.OwnerUserId == null)
            .OrderByDescending(r => r.IsDefault)
            .ThenBy(r => r.Name)
            .Select(r => new RulePresetOption(r.Id, r.Name, r.IsDefault))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
