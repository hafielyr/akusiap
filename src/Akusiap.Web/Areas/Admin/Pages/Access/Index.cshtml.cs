using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Access;

/// <summary>
/// <c>/admin/akses</c> (ENT-03): find a user by exact email, list their purchases and subscriptions, and grant, change
/// or end access. Plain forms that redirect back to the same user (no JavaScript).
/// </summary>
public sealed class IndexModel(GetUserAccess getAccess, ManageAccess manage, IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/admin/akses";

    [BindProperty(SupportsGet = true, Name = "email")]
    public string? Email { get; set; }

    public UserAccess? Access { get; private set; }

    public GrantOptions Options { get; private set; } = new([], []);

    /// <summary>Problems from the last action, already translated.</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];

    public bool Searched => !string.IsNullOrWhiteSpace(Email);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Access = await getAccess.FindByEmailAsync(Email, cancellationToken);
        if (Access is not null)
        {
            Options = await getAccess.OptionsAsync(cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostGrantExamAsync(Guid userId, long examId, DateOnly? until, CancellationToken cancellationToken) =>
        await DoneAsync(userId, await manage.GrantExamAsync(new GrantExamRequest(userId, examId, until), cancellationToken), "Admin.Access.Granted", cancellationToken);

    public async Task<IActionResult> OnPostGrantPlanAsync(Guid userId, long planId, int days, CancellationToken cancellationToken) =>
        await DoneAsync(userId, await manage.GrantPlanAsync(new GrantPlanRequest(userId, planId, days), cancellationToken), "Admin.Access.Granted", cancellationToken);

    public async Task<IActionResult> OnPostChangeEndAsync(Guid userId, AccessKind kind, long id, DateOnly? until, CancellationToken cancellationToken)
    {
        var result = until is { } date
            ? await manage.ChangeEndAsync(new ChangeAccessEndRequest(userId, kind, id, date), cancellationToken)
            : Result.Fail<DateTime>(new Problem("Admin.Access.DateRequired"));
        return await DoneAsync(userId, result, "Admin.Access.Changed", cancellationToken);
    }

    public async Task<IActionResult> OnPostEndAsync(Guid userId, AccessKind kind, long id, CancellationToken cancellationToken) =>
        await DoneAsync(userId, await manage.EndAsync(new EndAccessRequest(userId, kind, id), cancellationToken), "Admin.Access.Ended", cancellationToken);

    private async Task<IActionResult> DoneAsync(Guid userId, Result<DateTime> result, string flash, CancellationToken cancellationToken)
    {
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData["Flash"] = flash;
            return Redirect(UrlFor(Email));
        }

        Access = await getAccess.FindByIdAsync(userId, cancellationToken);
        if (Access is null)
        {
            return NotFound();
        }

        Email = Access.User.Email;
        Options = await getAccess.OptionsAsync(cancellationToken);
        Errors = [.. result.Problems.Select(p => ProblemModelState.Describe(p, t))];
        return Page();
    }

    public static string UrlFor(string? email) =>
        string.IsNullOrWhiteSpace(email) ? Route : $"{Route}?email={Uri.EscapeDataString(email.Trim())}";
}
