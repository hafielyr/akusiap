using Akusiap.Application.Moderation;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Sessions;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Pages.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.SharedExams;

/// <summary>
/// <c>/s/{token}</c> (SHR-02, SHR-03, story G-07): the shared exam's landing page. Guests enter a name (shown to the
/// creator with their score) and start; signed-in users start straight away. A link that doesn't work is 404; a
/// taken-down exam shows "Ujian tidak tersedia".
/// </summary>
public sealed class LandingModel(ExamReports exams, StartAttempt startAttempt, IStringLocalizer<SharedResource> t) : PageModel
{
    public const int GuestNameMaxLength = 60;

    [BindProperty]
    public string? GuestName { get; set; }

    public SharedExamInfo Exam { get; private set; } = null!;

    public bool SignedIn => User.Identity?.IsAuthenticated == true;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        var exam = await exams.FindSharedAsync(token, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken cancellationToken)
    {
        var exam = await exams.FindSharedAsync(token, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        var name = SignedIn ? null : GuestName?.Trim();
        if (name is { Length: > GuestNameMaxLength })
        {
            ModelState.AddModelError(nameof(GuestName), t["Validation.StringLength", t["Shared.Name"].Value, GuestNameMaxLength].Value);
            return Page();
        }

        var guestToken = SignedIn ? (Guid?)null : GuestTokenCookie.GetOrCreate(HttpContext);
        var result = await startAttempt.HandleAsync(
            new StartAttemptRequest(exam.ExamId, AttemptMode.Practice, ShareToken: token, GuestToken: guestToken, GuestName: name),
            cancellationToken);
        if (result.Succeeded)
        {
            return Redirect(SessionRoutes.Player(result.Value!.AttemptId));
        }

        if (result.IsNotFound)
        {
            return NotFound();
        }

        foreach (var problem in result.Problems)
        {
            ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
        }

        return Page();
    }
}
