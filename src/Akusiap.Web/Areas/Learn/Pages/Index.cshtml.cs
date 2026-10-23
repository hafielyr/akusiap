using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary>
/// <c>/learn</c>: the learner's hub (BRD-03 bento): today's drill with the streak and level, then links to every learner
/// page, and until the email is confirmed a resend button (AC-02).
/// </summary>
public sealed class IndexModel(
    UserManager<User> users,
    AccountEmails emails,
    Application.Gamification.Badges badges,
    Application.Gamification.DailyDrills drills,
    Application.Abstractions.ICurrentUser currentUser) : PageModel
{
    public bool EmailConfirmed { get; private set; } = true;

    public Domain.Gamification.LevelProgress Level { get; private set; } = null!;

    public Domain.Gamification.StreakInfo Streak { get; private set; } = null!;

    public Application.Gamification.DrillToday Drill { get; private set; } = null!;

    /// <summary>The resource key of the message after a resend, if any.</summary>
    [TempData]
    public string? ResendMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        EmailConfirmed = user?.EmailConfirmed ?? true;
        (Level, Streak) = await badges.SummaryAsync(currentUser.UserId!.Value, cancellationToken);
        Drill = await drills.TodayAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostResendConfirmationAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is { EmailConfirmed: false })
        {
            var sent = await emails.SendConfirmationAsync(user, cancellationToken);
            ResendMessage = !sent ? "Account.Confirm.SendFailed" : emails.Delivery switch
            {
                Application.Abstractions.EmailDelivery.File => "Account.Confirm.SavedToFile",
                Application.Abstractions.EmailDelivery.Dropped => "Account.Confirm.NotSent",
                _ => "Account.Confirm.Resent",
            };
        }

        return Redirect("/learn");
    }
}
