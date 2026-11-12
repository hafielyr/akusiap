using Akusiap.Application.Catalog;
using Akusiap.Application.Planning;
using Akusiap.Application.UpcomingTests;
using Akusiap.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Tests;

/// <summary>
/// <c>/jadwal-tes/{slug}</c> (CPNS-02): one test's dates and countdown, what it covers, how to prepare, and its package
/// with the buttons that fit the viewer: buy or try for others, and the study tools (tryout, practice, flashcards, plan)
/// for learners with access. Unpublished tests answer 404.
/// </summary>
public sealed class DetailModel(UpcomingTests upcomingTests, GetCatalogExam getExam, StudyGoals goals) : PageModel
{
    public UpcomingTestDetail Test { get; private set; } = null!;

    /// <summary>The package exam when it is published (null otherwise).</summary>
    public CatalogExamDetail? Package { get; private set; }

    /// <summary>The viewer's goal for the package exam, if any.</summary>
    public StudyGoalView? Goal { get; private set; }

    public bool CanStudy => Package is { } p && p.Status == ExamStatus.Published && p.Decision.CanStart && p.Ctas.Contains(ExamCta.StartPractice);

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var test = await upcomingTests.GetAsync(slug, cancellationToken);
        if (test is null)
        {
            return NotFound();
        }

        Test = test;
        if (test.Test.PackageExamId is { } examId)
        {
            Package = await getExam.HandleAsync(examId, cancellationToken);
            if (CanStudy && User.Identity?.IsAuthenticated == true)
            {
                Goal = await goals.ForExamAsync(examId, cancellationToken);
            }
        }

        return Page();
    }

    /// <summary>The goal form, with the test day suggested when it lies in the future.</summary>
    public string GoalUrl(long examId)
    {
        var url = $"/learn/target?examId={examId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return Test.Test.TestStartsOn is { } day && Test.Test.DaysLeft > 0
            ? $"{url}&tanggal={day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}"
            : url;
    }
}
