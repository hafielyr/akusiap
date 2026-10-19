using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>
/// Loads the signed-in user's <see cref="AllowanceInputs"/> and evaluates <see cref="AllowancePolicy"/> (OWN-03, OWN-05).
/// Used questions are own questions that aren't Retired; used exams are all own exams.
/// </summary>
public sealed class GetAllowance(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<Allowance> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("The allowance needs a signed-in user.");
        var now = clock.UtcNow;
        var free = await db.Plans.Where(p => p.Code == PlanCode.Free)
            .Select(p => new PlanCaps(p.MaxOwnQuestions, p.MaxOwnExams, p.IncludesAuthoring))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? new PlanCaps(null, null);
        var plans = await db.Subscriptions
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now)
            .Select(s => new PlanCaps(s.Plan!.MaxOwnQuestions, s.Plan.MaxOwnExams, s.Plan.IncludesAuthoring))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var examAllowances = await db.ExamPurchases
            .Where(p => p.UserId == userId && p.Status == ExamPurchaseStatus.Active && p.AccessExpiresAt > now)
            .Select(p => p.Exam!.OwnQuestionAllowance)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var usedQuestions = await db.Questions.CountAsync(q => q.OwnerUserId == userId && q.Status != QuestionStatus.Retired, cancellationToken).ConfigureAwait(false);
        var usedExams = await db.Exams.CountAsync(e => e.OwnerUserId == userId, cancellationToken).ConfigureAwait(false);
        return AllowancePolicy.Evaluate(new AllowanceInputs(currentUser.IsAdmin, free, plans, examAllowances, usedQuestions, usedExams));
    }
}
