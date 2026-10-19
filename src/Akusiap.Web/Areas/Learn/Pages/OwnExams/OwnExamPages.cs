using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.OwnExams;
using Akusiap.Application.Sharing;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Web.Areas.Admin.Pages.Presets;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Areas.Learn.Pages.OwnExams;

/// <summary>The own-exam form (OWN-04). Limits are text so blank means "no limit", parsed like the admin preset form.</summary>
public sealed class OwnExamInput
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public bool Published { get; set; }

    public List<long> QuestionIds { get; set; } = [];

    public string? TimeLimitMinutes { get; set; }

    public string? QuestionCount { get; set; }

    public string? PassMarkPercent { get; set; }

    public bool HintsEnabled { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    public ShowAnswers? ShowAnswers { get; set; }

    public static OwnExamInput Blank() => new()
    {
        HintsEnabled = true,
        ShuffleQuestions = true,
        ShuffleOptions = true,
        ShowAnswers = Domain.Content.ShowAnswers.AfterSubmit,
    };

    public static OwnExamInput From(OwnExamDetail exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        return new OwnExamInput
        {
            Title = exam.Title,
            Description = exam.Description,
            Published = exam.Status == ExamStatus.Published,
            QuestionIds = [.. exam.QuestionIds],
            TimeLimitMinutes = exam.TimeLimitMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            QuestionCount = exam.QuestionCount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PassMarkPercent = exam.PassMarkPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HintsEnabled = exam.HintsEnabled,
            ShuffleQuestions = exam.ShuffleQuestions,
            ShuffleOptions = exam.ShuffleOptions,
            ShowAnswers = exam.ShowAnswers,
        };
    }

    public (SaveOwnExamRequest Request, List<Problem> NumberProblems) ToRequest(long? id)
    {
        var (rules, problems) = new PresetInput
        {
            Name = Title,
            TimeLimitMinutes = TimeLimitMinutes,
            QuestionCount = QuestionCount,
            PassMarkPercent = PassMarkPercent,
            HintsEnabled = HintsEnabled,
            ShuffleQuestions = ShuffleQuestions,
            ShuffleOptions = ShuffleOptions,
            ShowAnswers = ShowAnswers,
        }.ToRequest(null);
        var request = new SaveOwnExamRequest(
            id, Title, Description, Published, QuestionIds, rules.TimeLimitMinutes, rules.QuestionCount, HintsEnabled,
            ShuffleQuestions, ShuffleOptions, rules.PassMarkPercent, ShowAnswers);
        return (request, problems);
    }
}

/// <summary>What the form partial needs.</summary>
public sealed record OwnExamFormView(OwnExamInput Input, IReadOnlyList<OwnExamQuestionOption> Questions, long? ExamId, bool CanSave);

/// <summary><c>/learn/ujian-saya</c> (OWN-04): the learner's own exams.</summary>
public sealed class IndexModel(Application.OwnExams.OwnExams own, GetAllowance getAllowance) : PageModel
{
    public const string Route = "/learn/ujian-saya";

    public IReadOnlyList<OwnExamRow> Exams { get; private set; } = [];

    public Allowance Allowance { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Exams = await own.ListAsync(cancellationToken);
        Allowance = await getAllowance.HandleAsync(cancellationToken);
    }
}

/// <summary><c>/learn/ujian-saya/baru</c> (OWN-04).</summary>
public sealed class CreateModel(Application.OwnExams.OwnExams own, GetAllowance getAllowance, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public OwnExamInput Input { get; set; } = OwnExamInput.Blank();

    public IReadOnlyList<OwnExamQuestionOption> Questions { get; private set; } = [];

    public Allowance Allowance { get; private set; } = null!;

    public Task OnGetAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var (request, numberProblems) = Input.ToRequest(null);
        var result = numberProblems.Count > 0 ? Result.Fail<long>(numberProblems) : await own.SaveAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Own.Exams.Saved";
        return Redirect($"{IndexModel.Route}/{result.Value}");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Questions = await own.QuestionOptionsAsync(cancellationToken);
        Allowance = await getAllowance.HandleAsync(cancellationToken);
    }
}

/// <summary><c>/learn/ujian-saya/{id}</c> (OWN-04): edit, and start practice.</summary>
public sealed class EditModel(
    Application.OwnExams.OwnExams own,
    GetAllowance getAllowance,
    ShareExam share,
    IOptions<SiteOptions> site,
    IStringLocalizer<SharedResource> t) : PageModel
{
    /// <summary>The share panel (SHR-01); null while Features:Sharing is off.</summary>
    public ShareState? Share { get; private set; }

    public string? ShareLink { get; private set; }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public OwnExamInput Input { get; set; } = OwnExamInput.Blank();

    public IReadOnlyList<OwnExamQuestionOption> Questions { get; private set; } = [];

    public Allowance Allowance { get; private set; } = null!;

    public ExamStatus Status { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var exam = await own.GetAsync(Id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Input = OwnExamInput.From(exam);
        Status = exam.Status;
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var exam = await own.GetAsync(Id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        var (request, numberProblems) = Input.ToRequest(Id);
        var result = numberProblems.Count > 0 ? Result.Fail<long>(numberProblems) : await own.SaveAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            Status = exam.Status;
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Own.Exams.Saved";
        return Redirect($"{IndexModel.Route}/{Id}");
    }

    public Task<IActionResult> OnPostShareAsync(CancellationToken cancellationToken) => ChangeShareAsync(share.ShareAsync, cancellationToken);

    public Task<IActionResult> OnPostStopShareAsync(CancellationToken cancellationToken) => ChangeShareAsync(share.StopAsync, cancellationToken);

    public Task<IActionResult> OnPostResetShareAsync(CancellationToken cancellationToken) => ChangeShareAsync(share.ResetAsync, cancellationToken);

    private async Task<IActionResult> ChangeShareAsync(Func<long, CancellationToken, Task<Result<ShareState>>> change, CancellationToken cancellationToken)
    {
        var result = await change(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = result.Succeeded ? "Share.Saved" : result.Problems[0].Code;
        return Redirect($"{IndexModel.Route}/{Id}");
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Questions = await own.QuestionOptionsAsync(cancellationToken);
        Allowance = await getAllowance.HandleAsync(cancellationToken);
        Share = await share.GetAsync(Id, cancellationToken);
        if (Share is { IsShared: true, ShareToken: { } token })
        {
            var baseUrl = string.IsNullOrWhiteSpace(site.Value.BaseUrl) ? $"{Request.Scheme}://{Request.Host}" : site.Value.BaseUrl.TrimEnd('/');
            ShareLink = $"{baseUrl}/s/{token}";
        }
    }
}
