using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Questions;

/// <summary>Filters for the admin question list; all optional. <paramref name="Page"/> is 1-based.</summary>
public sealed record QuestionFilter(
    long? SubjectId = null,
    long? TopicId = null,
    QuestionStatus? Status = null,
    QuestionType? Type = null,
    string? Search = null,
    int Page = 1);

public sealed record QuestionRow(
    long Id,
    string Text,
    string SubjectName,
    string TopicName,
    QuestionType Type,
    QuestionStatus Status,
    short? Difficulty,
    DateTime UpdatedAt);

public sealed record QuestionPage(IReadOnlyList<QuestionRow> Rows, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>App questions (no owner), newest change first, filtered and paged.</summary>
public sealed class ListQuestions(IAppDbContext db)
{
    public const int PageSize = 20;

    public async Task<QuestionPage> HandleAsync(QuestionFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = db.Questions.Where(q => q.OwnerUserId == null);
        if (filter.TopicId is { } topicId)
        {
            query = query.Where(q => q.TopicId == topicId);
        }
        else if (filter.SubjectId is { } subjectId)
        {
            query = query.Where(q => q.Topic!.SubjectId == subjectId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(q => q.Status == status);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(q => q.QuestionType == type);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
            var term = filter.Search.Trim().ToLower();
            query = query.Where(q => q.Text.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(q => q.UpdatedAt)
            .ThenByDescending(q => q.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(q => new QuestionRow(q.Id, q.Text, q.Topic!.Subject!.Name, q.Topic.Name, q.QuestionType, q.Status, q.Difficulty, q.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new QuestionPage(rows, page, PageSize, total);
    }
}
