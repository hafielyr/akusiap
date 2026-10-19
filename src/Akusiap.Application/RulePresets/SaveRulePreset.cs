using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.RulePresets;

/// <summary>
/// Saves a system rule preset (ADM-T5). There is always exactly one system default: making a preset the default unsets
/// the old one in the same transaction (the partial unique index allows only one), and the current default can't be
/// switched off directly; another preset has to become the default instead.
/// </summary>
public sealed class SaveRulePreset(IAppDbContext db)
{
    public const string NameTaken = "Admin.Presets.NameTaken";
    public const string DefaultRequired = "Admin.Presets.DefaultRequired";
    public const string IsDefaultField = "IsDefault";

    public async Task<Result<long>> HandleAsync(SaveRulePresetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = RulePresetValidator.Validate(request);
        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        var name = TextRules.Clean(request.Name)!;
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
        var lowered = name.ToLower();
        var taken = await db.RulePresets
            .AnyAsync(r => r.OwnerUserId == null && r.Name.ToLower() == lowered && r.Id != request.Id, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1304, CA1311, CA1862
        if (taken)
        {
            return Result.Fail<long>(new Problem(NameTaken, RulePresetValidator.NameField));
        }

        RulePreset preset;
        if (request.Id is { } id)
        {
            var existing = await db.RulePresets
                .SingleOrDefaultAsync(r => r.Id == id && r.OwnerUserId == null, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }

            if (existing.IsDefault && !request.IsDefault)
            {
                return Result.Fail<long>(new Problem(DefaultRequired, IsDefaultField));
            }

            preset = existing;
        }
        else
        {
            preset = new RulePreset();
            db.RulePresets.Add(preset);
        }

        var becomesDefault = request.IsDefault && !preset.IsDefault;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (becomesDefault)
        {
            await db.RulePresets
                .Where(r => r.OwnerUserId == null && r.IsDefault)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.IsDefault, false), cancellationToken)
                .ConfigureAwait(false);
        }

        preset.Name = name;
        preset.TimeLimitMinutes = request.TimeLimitMinutes;
        preset.QuestionCount = request.QuestionCount;
        preset.HintsEnabled = request.HintsEnabled;
        preset.ShuffleQuestions = request.ShuffleQuestions;
        preset.ShuffleOptions = request.ShuffleOptions;
        preset.MaxAttempts = request.MaxAttempts;
        preset.PassMarkPercent = request.PassMarkPercent;
        preset.ShowAnswers = request.ShowAnswers!.Value;
        preset.IsDefault = request.IsDefault;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(preset.Id);
    }
}
