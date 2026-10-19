using System.Globalization;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Plans;

/// <summary>A plan's fair-use caps. Null means "not configured", which the app treats as blocked, never as unlimited.</summary>
public sealed record PlanLimitsRow(long PlanId, PlanCode Code, string Name, int? MaxOwnQuestions, int? MaxOwnExams, int? MaxImportRows);

/// <summary>What the admin typed: raw text, so "abc" is reported instead of silently becoming "not configured".</summary>
public sealed record SavePlanLimitsRequest(long PlanId, string? MaxOwnQuestions, string? MaxOwnExams, string? MaxImportRows);

/// <summary>Every plan with its caps (OWN-01, story ADM-29), in plan order.</summary>
public sealed class ListPlanLimits(IAppDbContext db)
{
    public async Task<IReadOnlyList<PlanLimitsRow>> HandleAsync(CancellationToken cancellationToken) =>
        [.. (await db.Plans
            .Select(p => new PlanLimitsRow(p.Id, p.Code, p.Name, p.MaxOwnQuestions, p.MaxOwnExams, p.MaxImportRows))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .OrderBy(p => p.Code)];
}

/// <summary>
/// Saves a plan's caps (OWN-01, D-59): whole numbers 0 to <see cref="MaxValue"/>, or empty for "not configured". The
/// values are read from the database on every request, so a change applies at once. Each change is audited.
/// </summary>
public sealed class SavePlanLimits(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const int MaxValue = 1_000_000;

    /// <summary>Args: field label key, maximum.</summary>
    public const string Invalid = "Admin.Limits.Invalid";

    public const string QuestionsLabel = "Admin.Limits.Questions";
    public const string ExamsLabel = "Admin.Limits.Exams";
    public const string ImportLabel = "Admin.Limits.ImportRows";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<PlanLimitsRow>> HandleAsync(SavePlanLimitsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null)
        {
            return Result.Fail<PlanLimitsRow>(Problem.NotFound);
        }

        var problems = new List<Problem>();
        var questions = Parse(request.MaxOwnQuestions, "MaxOwnQuestions", QuestionsLabel, problems);
        var exams = Parse(request.MaxOwnExams, "MaxOwnExams", ExamsLabel, problems);
        var rows = Parse(request.MaxImportRows, "MaxImportRows", ImportLabel, problems);
        if (problems.Count > 0)
        {
            return Result.Fail<PlanLimitsRow>(problems);
        }

        var before = new { plan.MaxOwnQuestions, plan.MaxOwnExams, plan.MaxImportRows };
        plan.MaxOwnQuestions = questions;
        plan.MaxOwnExams = exams;
        plan.MaxImportRows = rows;
        var after = new { plan.MaxOwnQuestions, plan.MaxOwnExams, plan.MaxImportRows };
        if (before != after)
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = currentUser.UserId ?? throw new InvalidOperationException("Limit changes need a signed-in admin."),
                Action = "Plan.Limits",
                EntityName = nameof(Plan),
                EntityId = plan.Id.ToString(CultureInfo.InvariantCulture),
                Changes = JsonSerializer.Serialize(new { before, after }, Json),
                CreatedAt = clock.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(new PlanLimitsRow(plan.Id, plan.Code, plan.Name, questions, exams, rows));
    }

    private static int? Parse(string? text, string field, string label, List<Problem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value <= MaxValue)
        {
            return value;
        }

        problems.Add(new Problem(Invalid, field, label, MaxValue));
        return null;
    }
}
