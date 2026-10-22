using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Catalog;

/// <summary>A piece of question text with the matched words located for highlighting.</summary>
public sealed record SearchSnippet(string Text, int MatchStart, int MatchLength)
{
    private const string Ellipsis = "…";

    /// <summary>
    /// Up to <paramref name="length"/> characters of <paramref name="text"/> (whitespace collapsed) around the first
    /// case-insensitive match of <paramref name="term"/>, with "…" where text was cut. Without a match, the start.
    /// </summary>
    public static SearchSnippet Around(string text, string term, int length)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(term);
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var at = term.Length == 0 ? -1 : flat.IndexOf(term.Trim(), StringComparison.OrdinalIgnoreCase);
        var matchLength = at < 0 ? 0 : term.Trim().Length;
        if (flat.Length <= length)
        {
            return new SearchSnippet(flat, Math.Max(at, 0), matchLength);
        }

        // Keep about a third of the window before the match so the reader sees what leads up to it.
        var start = at < 0 ? 0 : Math.Clamp(at - (length / 3), 0, flat.Length - length);
        var piece = flat.Substring(start, length);
        var prefix = start > 0 ? Ellipsis : string.Empty;
        var suffix = start + length < flat.Length ? Ellipsis : string.Empty;
        return new SearchSnippet(prefix + piece + suffix, at < 0 ? 0 : at - start + prefix.Length, matchLength);
    }
}

/// <summary>One question that matched, shown by its stem only (never options, answers or explanations).</summary>
public sealed record QuestionHit(long QuestionId, SearchSnippet Snippet, long ExamId, string ExamTitle, string? SubjectName, string TopicName, bool IsTrial);

public sealed record SearchResults(string Query, IReadOnlyList<CatalogItem> Exams, int ExamTotal, IReadOnlyList<QuestionHit> Questions, int QuestionTotal);

/// <summary>
/// Search across the catalog (BRD-04, D-105): exams by title or description (the catalog's own search) and questions by
/// their text or topic name. Only Active questions of Published app exams are searched, so drafts, learners' own exams and retired
/// questions never show. A question in several exams is listed once, under the first exam by title.
/// </summary>
public sealed class SearchCatalog(IAppDbContext db, ListCatalog catalog)
{
    public const int MinLength = 2;
    public const int MaxLength = 100;
    public const int MaxExams = 6;
    public const int MaxQuestions = 20;
    public const int SnippetLength = 160;

    /// <summary>The results, or null when the query is too short to search.</summary>
    public async Task<SearchResults?> HandleAsync(string? query, CancellationToken cancellationToken)
    {
        var term = query?.Trim();
        if (term is null || term.Length < MinLength)
        {
            return null;
        }

        if (term.Length > MaxLength)
        {
            term = term[..MaxLength];
        }

        var exams = await catalog.HandleAsync(new CatalogFilter(term), cancellationToken).ConfigureAwait(false);
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
        var lowered = term.ToLower();
        var matches = db.ExamQuestions.AsNoTracking().Where(eq =>
            eq.Exam!.OwnerKind == OwnerKind.App
            && eq.Exam.Status == ExamStatus.Published
            && eq.Question!.Status == QuestionStatus.Active
            && (eq.Question.Text.ToLower().Contains(lowered) || eq.Question.Topic!.Name.ToLower().Contains(lowered)));
#pragma warning restore CA1304, CA1311, CA1862
        var total = await matches.Select(eq => eq.QuestionId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await matches
            .OrderBy(eq => eq.Exam!.Title)
            .ThenBy(eq => eq.SortOrder)
            .Take(MaxQuestions * 3)
            .Select(eq => new
            {
                eq.QuestionId,
                eq.Question!.Text,
                eq.ExamId,
                ExamTitle = eq.Exam!.Title,
                SubjectName = eq.Exam.Subject == null ? null : eq.Exam.Subject.Name,
                TopicName = eq.Question.Topic!.Name,
                eq.IsTrial,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var hits = rows
            .DistinctBy(r => r.QuestionId)
            .Take(MaxQuestions)
            .Select(r => new QuestionHit(r.QuestionId, SearchSnippet.Around(r.Text, term, SnippetLength), r.ExamId, r.ExamTitle, r.SubjectName, r.TopicName, r.IsTrial))
            .ToList();
        return new SearchResults(term, [.. exams.Items.Take(MaxExams)], exams.Total, hits, total);
    }
}
