using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Sessions;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Pages.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.Exams;

/// <summary>The setup form. Practice fields are ignored in Simulation, which always uses the exam's rules.</summary>
public sealed class SetupForm
{
    public AttemptMode Mode { get; set; } = AttemptMode.Practice;

    public int? QuestionCount { get; set; }

    public bool Untimed { get; set; }

    public int? TimeLimitMinutes { get; set; }

    public bool HintsEnabled { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    public List<long> TopicIds { get; set; } = [];
}

/// <summary>
/// <c>/ujian/{id}/mulai</c> (SES-02, stories S-02, S-03): choose Practice or Simulation and, for Practice, the question
/// count, topics, time limit, hints and shuffling within the exam's bounds (D-12). Every value is checked again on the
/// server by <see cref="StartAttempt"/>. Viewers who can't start are sent to the exam page; guests get the guest cookie.
/// </summary>
public sealed class StartModel(GetSessionSetup getSetup, StartAttempt startAttempt, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public SetupForm Form { get; set; } = new();

    public SessionSetup Setup { get; private set; } = null!;

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (await LoadAsync(id, cancellationToken) is { } redirect)
        {
            return redirect;
        }

        Form = new SetupForm
        {
            QuestionCount = Setup.DefaultQuestionCount,
            Untimed = Setup.Preset.TimeLimitMinutes is null,
            TimeLimitMinutes = Setup.Preset.TimeLimitMinutes,
            HintsEnabled = Setup.Preset.HintsEnabled,
            ShuffleQuestions = Setup.Preset.ShuffleQuestions,
            ShuffleOptions = Setup.Preset.ShuffleOptions,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        if (await LoadAsync(id, cancellationToken) is { } redirect)
        {
            return redirect;
        }

        ModelState.Clear(); // StartAttempt checks every value and reports problems in the user's language.
        var overrides = Form.Mode == AttemptMode.Simulation
            ? RuleOverrides.None
            : new RuleOverrides(
                QuestionCount: Form.QuestionCount,
                TimeLimitMinutes: Form.Untimed ? null : Form.TimeLimitMinutes,
                Untimed: Form.Untimed ? true : null,
                HintsEnabled: Form.HintsEnabled,
                ShuffleQuestions: Form.ShuffleQuestions,
                ShuffleOptions: Form.ShuffleOptions,
                TopicIds: Form.TopicIds);
        if (Form.Mode == AttemptMode.Practice && !Form.Untimed && Form.TimeLimitMinutes is null)
        {
            overrides = overrides with { Untimed = true };
        }

        var guestToken = User.Identity?.IsAuthenticated == true ? (Guid?)null : GuestTokenCookie.GetOrCreate(HttpContext);
        var result = await startAttempt.HandleAsync(new StartAttemptRequest(id, Form.Mode, overrides, GuestToken: guestToken), cancellationToken);
        if (result.Succeeded)
        {
            return Redirect(SessionRoutes.Player(result.Value!.AttemptId));
        }

        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (result.Problems.Any(p => p.Code == StartAttempt.NoAccess))
        {
            return Redirect(ExamRoutes.Detail(id));
        }

        ModelState.AddProblems(result.Problems.Where(p => p.Field is not null), t, nameof(Form));
        Errors = [.. result.Problems.Where(p => p.Field is null).Select(p => ProblemModelState.Describe(p, t))];
        return Page();
    }

    /// <summary>Loads the setup, or returns 404 / the exam page when this viewer can't start the exam.</summary>
    private async Task<IActionResult?> LoadAsync(long id, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        var setup = await getSetup.HandleAsync(id, guestToken, cancellationToken);
        if (setup is null)
        {
            return NotFound();
        }

        if (!setup.Decision.CanStart)
        {
            return Redirect(ExamRoutes.Detail(id));
        }

        Setup = setup;
        return null;
    }
}
