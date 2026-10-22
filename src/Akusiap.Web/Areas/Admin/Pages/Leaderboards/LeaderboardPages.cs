using System.Globalization;
using Akusiap.Application.Common;
using Akusiap.Application.Gamification;
using Akusiap.Domain.Gamification;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Leaderboards;

/// <summary><c>/admin/papan-peringkat</c> (DRL-02): every catalog exam with its leaderboard settings.</summary>
public sealed class IndexModel(LeaderboardAdmin admin) : PageModel
{
    public IReadOnlyList<LeaderboardExamRow> Exams { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => Exams = await admin.ListAsync(cancellationToken);
}

/// <summary><c>/admin/papan-peringkat/{examId}</c> (DRL-02): one exam's leaderboard settings and the history reset.</summary>
public sealed class EditModel(LeaderboardAdmin admin, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long ExamId { get; set; }

    [BindProperty]
    public LeaderboardInput Input { get; set; } = new();

    public LeaderboardAdminDetail Detail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = LeaderboardInput.From(Detail.Settings);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var (request, numberProblems) = Input.ToRequest(ExamId);
        var result = numberProblems.Count == 0 ? await admin.SaveAsync(request, cancellationToken) : null;
        if (result is { IsNotFound: true })
        {
            return NotFound();
        }

        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Saved";
            return Redirect(PageUrl(ExamId));
        }

        ModelState.AddProblems(result?.Problems ?? numberProblems, t, nameof(Input));
        return await LoadAsync(cancellationToken) ? Page() : NotFound();
    }

    public async Task<IActionResult> OnPostResetAsync(CancellationToken cancellationToken)
    {
        var result = await admin.ResetHistoryAsync(ExamId, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = "Admin.Leaderboards.ResetDone";
        return Redirect(PageUrl(ExamId));
    }

    public static string PageUrl(long examId) => $"/admin/papan-peringkat/{examId}";

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var detail = await admin.GetAsync(ExamId, cancellationToken);
        if (detail is null)
        {
            return false;
        }

        Detail = detail;
        return true;
    }
}

/// <summary>Leaderboard settings fields, posted as <c>Input.*</c>. Minimums are text so blank means "no minimum".</summary>
public sealed class LeaderboardInput
{
    public bool Enabled { get; set; }

    public bool WeeklyReset { get; set; }

    public bool AwardRank1 { get; set; }

    public bool AwardTop3 { get; set; }

    public bool AwardTop10 { get; set; }

    public string? MinScorePercent { get; set; }

    public string? MinParticipants { get; set; }

    public static LeaderboardInput From(LeaderboardSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new LeaderboardInput
        {
            Enabled = settings.Enabled,
            WeeklyReset = settings.WeeklyReset,
            AwardRank1 = settings.AwardRank1,
            AwardTop3 = settings.AwardTop3,
            AwardTop10 = settings.AwardTop10,
            MinScorePercent = settings.MinScorePercent?.ToString(CultureInfo.InvariantCulture),
            MinParticipants = settings.MinParticipants?.ToString(CultureInfo.InvariantCulture),
        };
    }

    public (SaveLeaderboardSettingsRequest Request, List<Problem> NumberProblems) ToRequest(long examId)
    {
        var problems = new List<Problem>();
        var request = new SaveLeaderboardSettingsRequest(
            examId,
            Enabled,
            WeeklyReset,
            AwardRank1,
            AwardTop3,
            AwardTop10,
            Parse(MinScorePercent, LeaderboardAdmin.MinScoreField, LeaderboardAdmin.MinScoreLabel, problems),
            Parse(MinParticipants, LeaderboardAdmin.MinParticipantsField, LeaderboardAdmin.MinParticipantsLabel, problems));
        return (request, problems);
    }

    private static int? Parse(string? text, string field, string label, List<Problem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add(new Problem(Presets.PresetInput.WholeNumber, field, label));
        return null;
    }
}
