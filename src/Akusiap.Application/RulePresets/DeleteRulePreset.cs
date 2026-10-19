using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.RulePresets;

/// <summary>Deletes a system preset that no exam uses and that is not the default (ADM-T5).</summary>
public sealed class DeleteRulePreset(IAppDbContext db)
{
    public const string DeleteDefault = "Admin.Presets.DeleteDefault";

    /// <summary>Arg: exam count.</summary>
    public const string InUse = "Admin.Presets.InUse";

    public async Task<Result<bool>> HandleAsync(long id, CancellationToken cancellationToken)
    {
        var preset = await db.RulePresets
            .SingleOrDefaultAsync(r => r.Id == id && r.OwnerUserId == null, cancellationToken)
            .ConfigureAwait(false);
        if (preset is null)
        {
            return Result.Fail<bool>(Problem.NotFound);
        }

        if (preset.IsDefault)
        {
            return Result.Fail<bool>(new Problem(DeleteDefault));
        }

        var exams = await db.Exams.CountAsync(e => e.RulePresetId == id, cancellationToken).ConfigureAwait(false);
        if (exams > 0)
        {
            return Result.Fail<bool>(new Problem(InUse, null, exams));
        }

        db.RulePresets.Remove(preset);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(true);
    }
}
