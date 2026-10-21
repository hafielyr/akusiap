using System.Globalization;
using Akusiap.Application.Classes;
using Akusiap.Application.Sessions;
using Akusiap.Domain;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Pages.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using ClassService = Akusiap.Application.Classes.Classes;

namespace Akusiap.Web.Areas.Learn.Pages.Classes;

public static class ClassRoutes
{
    public const string Index = "/learn/kelas";

    public static string Join(string? code = null) => code is null ? $"{Index}/gabung" : $"{Index}/gabung?kode={Uri.EscapeDataString(code)}";

    public static string Detail(long classId) => $"{Index}/{classId.ToString(CultureInfo.InvariantCulture)}";

    public static string Assignment(long classId, long assignmentId) => $"{Detail(classId)}/tugas/{assignmentId.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary><c>/learn/kelas</c> (GAM-01): the classes you teach and the ones you joined; teachers create classes here.</summary>
public sealed class IndexModel(ClassService classes, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public string? Name { get; set; }

    public IReadOnlyList<ClassSummary> Classes { get; private set; } = [];

    public bool IsTeacher => User.IsInRole(Domain.Users.RoleNames.Teacher) || User.IsInRole(Domain.Users.RoleNames.Admin);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Classes = await classes.MineAsync(cancellationToken);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var result = await classes.CreateAsync(Name, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
            }

            Classes = await classes.MineAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Class.Created";
        return Redirect(ClassRoutes.Detail(result.Value));
    }
}

/// <summary><c>/learn/kelas/gabung?kode=…</c> (T-10): join a class with its code; the invite link fills the code in.</summary>
public sealed class JoinModel(ClassService classes, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "kode")]
    public string? Code { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await classes.JoinAsync(Code, cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
            }

            return Page();
        }

        TempData["Flash"] = "Class.Joined";
        return Redirect(ClassRoutes.Detail(result.Value));
    }
}

/// <summary>The assign form: one of the teacher's exams and an open window in Jakarta time.</summary>
public sealed class AssignInput
{
    public long? ExamId { get; set; }

    public DateTime? OpensAt { get; set; }

    public DateTime? ClosesAt { get; set; }
}

/// <summary>
/// <c>/learn/kelas/{id}</c> (T-09 to T-13): students see the assignments and start them; the teacher also sees the
/// invite code, the students (and removes them) and assigns exams.
/// </summary>
public sealed class DetailModel(ClassService classes, StartAttempt startAttempt, Application.Abstractions.IClock clock, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public AssignInput Assign { get; set; } = new();

    public ClassDetail Class { get; private set; } = null!;

    public IReadOnlyList<AssignableExam> Exams { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        var now = JakartaTime.ToLocal(clock.UtcNow);
        Assign.OpensAt ??= new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Unspecified);
        Assign.ClosesAt ??= Assign.OpensAt.Value.AddDays(7);
        return Page();
    }

    public async Task<IActionResult> OnPostAssignAsync(CancellationToken cancellationToken)
    {
        var result = await classes.AssignAsync(
            Id,
            Assign.ExamId,
            Assign.OpensAt is { } opens ? JakartaTime.FromLocal(opens) : null,
            Assign.ClosesAt is { } closes ? JakartaTime.FromLocal(closes) : null,
            cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field is null ? string.Empty : $"{nameof(Assign)}.{problem.Field}", ProblemModelState.Describe(problem, t));
            }

            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Class.Assigned";
        return Redirect(ClassRoutes.Detail(Id));
    }

    public async Task<IActionResult> OnPostRemoveAsync(Guid studentId, CancellationToken cancellationToken)
    {
        if (!await classes.RemoveMemberAsync(Id, studentId, cancellationToken))
        {
            return NotFound();
        }

        TempData["Flash"] = "Class.StudentRemoved";
        return Redirect(ClassRoutes.Detail(Id));
    }

    /// <summary>Starts an assignment as a Simulation with the class's locked rules.</summary>
    public async Task<IActionResult> OnPostStartAsync(long assignmentId, long examId, CancellationToken cancellationToken)
    {
        var result = await startAttempt.HandleAsync(new StartAttemptRequest(examId, Domain.Sessions.AttemptMode.Simulation, ClassAssignmentId: assignmentId), cancellationToken);
        if (result.Succeeded)
        {
            return Redirect(SessionRoutes.Player(result.Value!.AttemptId));
        }

        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = result.Problems[0].Code;
        return Redirect(ClassRoutes.Detail(Id));
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (await classes.GetAsync(Id, cancellationToken) is not { } detail)
        {
            return false;
        }

        Class = detail;
        if (detail.IsTeacher)
        {
            Exams = await classes.AssignableExamsAsync(cancellationToken);
        }

        return true;
    }
}

/// <summary><c>/learn/kelas/{id}/tugas/{assignmentId}</c> (T-14): the teacher's results per student and the class leaderboard.</summary>
public sealed class AssignmentModel(ClassService classes) : PageModel
{
    public AssignmentResults Results { get; private set; } = null!;

    public long ClassId { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, long assignmentId, CancellationToken cancellationToken)
    {
        if (await classes.ResultsAsync(id, assignmentId, cancellationToken) is not { } results)
        {
            return NotFound();
        }

        ClassId = id;
        Results = results;
        return Page();
    }
}
