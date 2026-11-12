using Akusiap.Application.Abstractions;
using Akusiap.Domain;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.UpcomingTests;

/// <summary>A test on <c>/jadwal-tes</c> (CPNS-01).</summary>
/// <param name="DaysLeft">Days from today (Jakarta) to the test day, or to the registration day without one.</param>
/// <param name="PackagePriceIdr">The linked package's price when it is a published paid app exam.</param>
public sealed record UpcomingTestItem(
    long Id,
    string Slug,
    string Name,
    string Organizer,
    UpcomingTestCategory Category,
    string? Summary,
    DateOnly? RegistrationOpensOn,
    DateOnly? TestStartsOn,
    ScheduleStatus ScheduleStatus,
    string? ScheduleNote,
    int? DaysLeft,
    long? PackageExamId,
    long? PackagePriceIdr);

/// <summary>A test's own page (CPNS-02): the list fields plus what it covers and where the dates come from.</summary>
public sealed record UpcomingTestDetail(UpcomingTestItem Test, IReadOnlyList<OutlineBlock> Outline, string? SourceUrl);

/// <summary>The public list of upcoming hiring tests (CPNS-01, D-129) and one test's page (CPNS-02).</summary>
public sealed class UpcomingTests(IAppDbContext db, IClock clock)
{
    public const int MaxQueryLength = 100;

    /// <summary>
    /// Published tests whose date is today or later, or that have no date yet, soonest first (no date last). The query
    /// matches name, organiser or summary, ignoring case; the category narrows the list.
    /// </summary>
    public async Task<IReadOnlyList<UpcomingTestItem>> ListAsync(string? query, UpcomingTestCategory? category, CancellationToken cancellationToken)
    {
        var today = JakartaTime.Today(clock.UtcNow);
        var tests = await Published().ToListAsync(cancellationToken).ConfigureAwait(false);
        var q = query?.Trim();
        if (q is { Length: > MaxQueryLength })
        {
            q = q[..MaxQueryLength];
        }

        return [.. tests
            .Where(t => category is null || t.Test.Category == category)
            .Where(t => string.IsNullOrEmpty(q) || Matches(t.Test, q))
            .Where(t => t.Test.KeyDate is not { } date || date >= today)
            .OrderBy(t => t.Test.KeyDate is null)
            .ThenBy(t => t.Test.KeyDate)
            .ThenBy(t => t.Test.Name, StringComparer.CurrentCulture)
            .Select(t => ToItem(t, today))];
    }

    /// <summary>The published test with this slug (past ones too, so old links keep working); null otherwise.</summary>
    public async Task<UpcomingTestDetail?> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var row = await Published(t => t.Slug == slug).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var today = JakartaTime.Today(clock.UtcNow);
        return new UpcomingTestDetail(ToItem(row, today), UpcomingTest.ParseOutline(row.Test.Outline), row.Test.SourceUrl);
    }

    /// <summary>The soonest published test linked to <paramref name="examId"/>, for the exam page (CPNS-02).</summary>
    public async Task<UpcomingTestItem?> ForExamAsync(long examId, CancellationToken cancellationToken)
    {
        var today = JakartaTime.Today(clock.UtcNow);
        var rows = await Published(t => t.ExamId == examId).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OrderBy(t => t.Test.KeyDate is null).ThenBy(t => t.Test.KeyDate).Select(t => ToItem(t, today)).FirstOrDefault();
    }

    /// <summary>Published tests (narrowed by <paramref name="filter"/> before the projection, so it runs in SQL).</summary>
    private IQueryable<Row> Published(System.Linq.Expressions.Expression<Func<UpcomingTest, bool>>? filter = null) =>
        db.UpcomingTests.AsNoTracking()
            .Where(t => t.IsPublished)
            .Where(filter ?? (t => true))
            .Select(t => new Row(
                t,
                t.Exam != null && t.Exam.OwnerKind == OwnerKind.App && t.Exam.Status == ExamStatus.Published && !t.Exam.IsFree ? t.Exam.PriceIdr : null,
                t.Exam != null && t.Exam.OwnerKind == OwnerKind.App && t.Exam.Status == ExamStatus.Published));

    private static bool Matches(UpcomingTest test, string q) =>
        test.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || test.Organizer.Contains(q, StringComparison.CurrentCultureIgnoreCase)
        || (test.Summary?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false);

    private static UpcomingTestItem ToItem(Row row, DateOnly today)
    {
        var t = row.Test;
        return new(
            t.Id,
            t.Slug,
            t.Name,
            t.Organizer,
            t.Category,
            t.Summary,
            t.RegistrationOpensOn,
            t.TestStartsOn,
            t.ScheduleStatus,
            t.ScheduleNote,
            t.DaysLeft(today),
            row.PackagePublished ? t.ExamId : null,
            row.PackagePrice);
    }

    private sealed record Row(UpcomingTest Test, long? PackagePrice, bool PackagePublished);
}
