using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Questions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.UpcomingTests;

/// <summary>Create (Id null) or edit an upcoming test (CPNS-01).</summary>
public sealed record SaveUpcomingTestRequest(
    long? Id,
    string? Slug,
    string? Name,
    string? Organizer,
    UpcomingTestCategory? Category,
    string? Summary,
    DateOnly? RegistrationOpensOn,
    DateOnly? TestStartsOn,
    ScheduleStatus? ScheduleStatus,
    string? ScheduleNote,
    string? Outline,
    string? SourceUrl,
    long? ExamId,
    bool IsPublished);

/// <summary>A row on the admin list.</summary>
public sealed record UpcomingTestAdminRow(long Id, string Slug, string Name, UpcomingTestCategory Category, DateOnly? KeyDate, ScheduleStatus ScheduleStatus, bool IsPublished, string? ExamTitle);

/// <summary>An app exam an upcoming test can link to as its package.</summary>
public sealed record PackageExamOption(long Id, string Title, ExamStatus Status);

public static class UpcomingTestRules
{
    public const string SlugField = "Slug";
    public const string NameField = "Name";
    public const string OrganizerField = "Organizer";
    public const string CategoryField = "Category";
    public const string SummaryField = "Summary";
    public const string TestStartsOnField = "TestStartsOn";
    public const string ScheduleStatusField = "ScheduleStatus";
    public const string ScheduleNoteField = "ScheduleNote";
    public const string OutlineField = "Outline";
    public const string SourceUrlField = "SourceUrl";
    public const string ExamField = "ExamId";

    public const string SlugLabel = "Admin.UpcomingTest.Slug";
    public const string NameLabel = "Admin.UpcomingTest.Name";
    public const string OrganizerLabel = "Admin.UpcomingTest.Organizer";
    public const string CategoryLabel = "Admin.UpcomingTest.Category";
    public const string SummaryLabel = "Admin.UpcomingTest.Summary";
    public const string ScheduleStatusLabel = "Admin.UpcomingTest.ScheduleStatus";
    public const string ScheduleNoteLabel = "Admin.UpcomingTest.ScheduleNote";
    public const string OutlineLabel = "Admin.UpcomingTest.Outline";
    public const string SourceUrlLabel = "Admin.UpcomingTest.SourceUrl";

    public const string SlugInvalid = "Admin.UpcomingTests.SlugInvalid";
    public const string SlugTaken = "Admin.UpcomingTests.SlugTaken";
    public const string SourceUrlInvalid = "Admin.UpcomingTests.SourceUrlInvalid";
    public const string OfficialNeedsDate = "Admin.UpcomingTests.OfficialNeedsDate";
    public const string DatesOrder = "Admin.UpcomingTests.DatesOrder";
    public const string ExamMissing = "Admin.UpcomingTests.ExamMissing";

    public static List<Problem> Validate(SaveUpcomingTestRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<Problem>();
        var slug = TextRules.Clean(request.Slug);
        if (slug is null)
        {
            errors.Add(new Problem(TextRules.Required, SlugField, SlugLabel));
        }
        else if (!UpcomingTest.IsValidSlug(slug))
        {
            errors.Add(new Problem(SlugInvalid, SlugField, UpcomingTest.SlugMaxLength));
        }

        Require(errors, request.Name, NameField, NameLabel, UpcomingTest.NameMaxLength);
        Require(errors, request.Organizer, OrganizerField, OrganizerLabel, UpcomingTest.OrganizerMaxLength);
        Limit(errors, request.Summary, SummaryField, SummaryLabel, UpcomingTest.SummaryMaxLength);
        Limit(errors, request.ScheduleNote, ScheduleNoteField, ScheduleNoteLabel, UpcomingTest.ScheduleNoteMaxLength);
        Limit(errors, request.Outline, OutlineField, OutlineLabel, UpcomingTest.OutlineMaxLength);
        if (request.Category is null)
        {
            errors.Add(new Problem(TextRules.Required, CategoryField, CategoryLabel));
        }

        if (request.ScheduleStatus is null)
        {
            errors.Add(new Problem(TextRules.Required, ScheduleStatusField, ScheduleStatusLabel));
        }
        else if (request.ScheduleStatus == ScheduleStatus.Official && request.TestStartsOn is null && request.RegistrationOpensOn is null)
        {
            errors.Add(new Problem(OfficialNeedsDate, ScheduleStatusField));
        }

        if (request.RegistrationOpensOn is { } opens && request.TestStartsOn is { } test && test < opens)
        {
            errors.Add(new Problem(DatesOrder, TestStartsOnField));
        }

        if (TextRules.Clean(request.SourceUrl) is { } url)
        {
            if (url.Length > UpcomingTest.SourceUrlMaxLength)
            {
                errors.Add(new Problem(TextRules.TooLong, SourceUrlField, SourceUrlLabel, UpcomingTest.SourceUrlMaxLength));
            }
            else if (!QuestionValidator.IsWebLink(url))
            {
                errors.Add(new Problem(SourceUrlInvalid, SourceUrlField));
            }
        }

        return errors;
    }

    private static void Require(List<Problem> errors, string? value, string field, string label, int max)
    {
        if (TextRules.Clean(value) is not { } text)
        {
            errors.Add(new Problem(TextRules.Required, field, label));
        }
        else if (text.Length > max)
        {
            errors.Add(new Problem(TextRules.TooLong, field, label, max));
        }
    }

    private static void Limit(List<Problem> errors, string? value, string field, string label, int max)
    {
        if (TextRules.Clean(value) is { } text && text.Length > max)
        {
            errors.Add(new Problem(TextRules.TooLong, field, label, max));
        }
    }
}

/// <summary>Admin upkeep of the upcoming tests list (CPNS-01): list, load, save and delete.</summary>
public sealed class ManageUpcomingTests(IAppDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<UpcomingTestAdminRow>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await db.UpcomingTests.AsNoTracking()
            .Select(t => new { t.Id, t.Slug, t.Name, t.Category, t.TestStartsOn, t.RegistrationOpensOn, t.ScheduleStatus, t.IsPublished, ExamTitle = t.Exam == null ? null : t.Exam.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows
            .Select(r => new UpcomingTestAdminRow(r.Id, r.Slug, r.Name, r.Category, r.TestStartsOn ?? r.RegistrationOpensOn, r.ScheduleStatus, r.IsPublished, r.ExamTitle))
            .OrderBy(r => r.KeyDate is null)
            .ThenBy(r => r.KeyDate)
            .ThenBy(r => r.Name, StringComparer.CurrentCulture)];
    }

    public Task<UpcomingTest?> GetAsync(long id, CancellationToken cancellationToken) =>
        db.UpcomingTests.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);

    /// <summary>App exams that can be a package, newest first.</summary>
    public async Task<IReadOnlyList<PackageExamOption>> ExamOptionsAsync(CancellationToken cancellationToken) =>
        await db.Exams.AsNoTracking()
            .Where(e => e.OwnerKind == OwnerKind.App && e.Status != ExamStatus.TakenDown)
            .OrderByDescending(e => e.Id)
            .Select(e => new PackageExamOption(e.Id, e.Title, e.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<Result<long>> SaveAsync(SaveUpcomingTestRequest request, CancellationToken cancellationToken)
    {
        var errors = UpcomingTestRules.Validate(request);
        if (errors.Count > 0)
        {
            return Result.Fail<long>(errors);
        }

        var slug = TextRules.Clean(request.Slug)!;
        if (await db.UpcomingTests.AnyAsync(t => t.Slug == slug && t.Id != request.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(UpcomingTestRules.SlugTaken, UpcomingTestRules.SlugField));
        }

        if (request.ExamId is { } examId
            && !await db.Exams.AnyAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.App && e.Status != ExamStatus.TakenDown, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(UpcomingTestRules.ExamMissing, UpcomingTestRules.ExamField));
        }

        var now = clock.UtcNow;
        UpcomingTest? test;
        if (request.Id is { } id)
        {
            test = await db.UpcomingTests.SingleOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
            if (test is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }
        }
        else
        {
            test = new UpcomingTest { CreatedAt = now };
            db.UpcomingTests.Add(test);
        }

        test.Slug = slug;
        test.Name = TextRules.Clean(request.Name)!;
        test.Organizer = TextRules.Clean(request.Organizer)!;
        test.Category = request.Category!.Value;
        test.Summary = TextRules.Clean(request.Summary);
        test.RegistrationOpensOn = request.RegistrationOpensOn;
        test.TestStartsOn = request.TestStartsOn;
        test.ScheduleStatus = request.ScheduleStatus!.Value;
        test.ScheduleNote = TextRules.Clean(request.ScheduleNote);
        test.Outline = TextRules.Clean(request.Outline);
        test.SourceUrl = TextRules.Clean(request.SourceUrl);
        test.ExamId = request.ExamId;
        test.IsPublished = request.IsPublished;
        test.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(test.Id);
    }

    public async Task<Result<bool>> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var test = await db.UpcomingTests.SingleOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (test is null)
        {
            return Result.Fail<bool>(Problem.NotFound);
        }

        db.UpcomingTests.Remove(test);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(true);
    }
}
