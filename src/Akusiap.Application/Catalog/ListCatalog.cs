using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Catalog;

/// <summary>Catalog filters; all optional. <paramref name="Page"/> is 1-based.</summary>
public sealed record CatalogFilter(string? Search = null, long? SubjectId = null, int Page = 1);

/// <summary>How a catalog card is labelled for the current viewer.</summary>
public enum CatalogBadge
{
    Price,
    Free,

    /// <summary>The viewer already has full access (purchase, subscription, owner or admin).</summary>
    YourAccess,
}

public sealed record CatalogItem(
    long Id,
    string Title,
    string? DescriptionExcerpt,
    string? SubjectName,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int ActiveQuestionCount,
    IReadOnlyList<string> Topics,
    CatalogBadge Badge);

public sealed record CatalogSubject(long Id, string Name);

public sealed record CatalogPage(IReadOnlyList<CatalogItem> Items, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>
/// The public exam catalog (CAT-01, story G-01): Published app exams only, newest first, 20 per page. The page of cards
/// is one query (plus a count); badges come from <see cref="IEntitlementService.DecideAsync"/> with the cached viewer.
/// </summary>
public sealed class ListCatalog(IAppDbContext db, IEntitlementService entitlements)
{
    public const int PageSize = 20;
    public const int ExcerptLength = 160;

    public async Task<CatalogPage> HandleAsync(CatalogFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = db.Exams.Where(e => e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published);
        if (filter.SubjectId is { } subjectId)
        {
            query = query.Where(e => e.SubjectId == subjectId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
            var term = filter.Search.Trim().ToLower();
            query = query.Where(e => e.Title.ToLower().Contains(term) || (e.Description != null && e.Description.ToLower().Contains(term)));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                SubjectName = e.Subject == null ? null : e.Subject.Name,
                e.IsFree,
                e.PriceIdr,
                e.AccessDays,
                e.OwnerUserId,
                e.OwnerKind,
                e.Status,
                e.IsShared,
                e.ShareToken,
                ActiveQuestionCount = e.Questions.Count(eq => eq.Question!.Status == QuestionStatus.Active),
                HasTrial = e.Questions.Any(eq => eq.IsTrial && eq.Question!.Status == QuestionStatus.Active),
                Topics = e.Questions
                    .Where(eq => eq.Question!.Status == QuestionStatus.Active)
                    .Select(eq => eq.Question!.Topic!.Name)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = new List<CatalogItem>(rows.Count);
        foreach (var row in rows)
        {
            var facts = new ExamAccessFacts(row.Id, row.OwnerUserId, row.OwnerKind, row.Status, row.IsFree, row.IsShared, row.ShareToken, row.HasTrial);
            var decision = await entitlements.DecideAsync(facts, shareToken: null, cancellationToken).ConfigureAwait(false);
            items.Add(new CatalogItem(
                row.Id,
                row.Title,
                Excerpt(row.Description),
                row.SubjectName,
                row.IsFree,
                row.PriceIdr,
                row.AccessDays,
                row.ActiveQuestionCount,
                row.Topics,
                BadgeFor(row.IsFree, decision)));
        }

        return new CatalogPage(items, page, PageSize, total);
    }

    /// <summary>Subjects that have at least one published app exam, for the catalog filter.</summary>
    public async Task<IReadOnlyList<CatalogSubject>> SubjectsAsync(CancellationToken cancellationToken) =>
        await db.Subjects
            .Where(s => db.Exams.Any(e => e.SubjectId == s.Id && e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published))
            .OrderBy(s => s.Name)
            .Select(s => new CatalogSubject(s.Id, s.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>"Akses Anda" for paid exams the viewer can fully use; free exams always say Gratis.</summary>
    public static CatalogBadge BadgeFor(bool isFree, AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (isFree)
        {
            return CatalogBadge.Free;
        }

        return decision.Level is AccessLevel.Full or AccessLevel.Admin or AccessLevel.Owner ? CatalogBadge.YourAccess : CatalogBadge.Price;
    }

    private static string? Excerpt(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var text = description.Trim();
        if (text.Length <= ExcerptLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', ExcerptLength);
        return string.Concat(text.AsSpan(0, cut > ExcerptLength / 2 ? cut : ExcerptLength).TrimEnd(), "…");
    }
}
