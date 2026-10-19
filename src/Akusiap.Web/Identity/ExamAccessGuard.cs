using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Akusiap.Web.Pages.Exams;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Identity;

public enum ExamAccessOutcome
{
    Allow,

    /// <summary>The exam doesn't exist, isn't published or was taken down: don't reveal it.</summary>
    NotFound,

    /// <summary>A guest who could get access by signing in or registering.</summary>
    SignIn,

    /// <summary>A signed-in user without access: send them to the exam page, which shows how to get it (CAT-02).</summary>
    ExamPage,
}

/// <summary>
/// <c>RequireExamAccess</c> for pages (ENT-02): asks <see cref="IEntitlementService"/> and turns a refusal into 404,
/// a sign-in redirect or a redirect to the exam page and its buy options (CAT-02). Pages that start an attempt must also check <see cref="AccessDecision.CanStart"/>.
/// </summary>
public static class ExamAccessGuard
{
    public static ExamAccessOutcome OutcomeFor(AccessDecision? decision, bool signedIn)
    {
        if (decision is null)
        {
            return ExamAccessOutcome.NotFound;
        }

        if (decision.Level != AccessLevel.None)
        {
            return ExamAccessOutcome.Allow;
        }

        return decision.Reason switch
        {
            AccessReason.NotPublished or AccessReason.TakenDown => ExamAccessOutcome.NotFound,
            _ when !signedIn => ExamAccessOutcome.SignIn,
            _ => ExamAccessOutcome.ExamPage,
        };
    }

    /// <summary>
    /// The entitlement for <paramref name="examId"/>, or the result to return instead when the viewer has no access.
    /// </summary>
    public static async Task<(EntitlementResult? Access, IActionResult? Denied)> RequireExamAccessAsync(
        this PageModel page, IEntitlementService entitlements, long examId, string? shareToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entitlements);
        var result = await entitlements.GetAsync(examId, shareToken, cancellationToken);
        var signedIn = page.User.Identity?.IsAuthenticated == true;
        return OutcomeFor(result?.Decision, signedIn) switch
        {
            ExamAccessOutcome.Allow => (result, null),
            ExamAccessOutcome.NotFound => (null, new NotFoundResult()),
            ExamAccessOutcome.SignIn => (null, new RedirectResult($"{AccountRoutes.SignIn}?ReturnUrl={Uri.EscapeDataString(page.Request.Path + page.Request.QueryString)}")),
            _ => (null, new RedirectResult(ExamRoutes.Detail(examId))),
        };
    }
}
