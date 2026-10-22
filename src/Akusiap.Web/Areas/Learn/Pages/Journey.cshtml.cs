using Akusiap.Application.Abstractions;
using Akusiap.Application.Gamification;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn/perjalanan</c> (GAM-01): the learner's own journey: level, streak, totals and badges.</summary>
public sealed class JourneyModel(Badges badges, ICurrentUser currentUser) : PageModel
{
    public const string Route = "/learn/perjalanan";

    public JourneyView Journey { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("The learn area needs a signed-in user.");
        // Catch up on milestones earned before badges existed, then show the journey.
        await badges.AwardAsync(userId, attemptId: null, cancellationToken);
        Journey = await badges.JourneyAsync(userId, cancellationToken);
    }
}
