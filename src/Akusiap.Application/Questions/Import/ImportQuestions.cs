using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Akusiap.Application.Questions.Import;

public sealed class ImportOptions
{
    public const string SectionName = "Import";

    /// <summary>The most data rows one admin upload may hold.</summary>
    public int MaxRows { get; set; } = 1000;
}

/// <summary>One row of the preview: what it will create, or why it can't.</summary>
public sealed record ImportRowPreview(int RowNumber, QuestionType? Type, string? Text, string? Topic, IReadOnlyList<Problem> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <param name="Key">Set when every row is valid: pass it to <see cref="ImportQuestions.ConfirmAsync"/>.</param>
public sealed record ImportPreview(Guid? Key, IReadOnlyList<ImportRowPreview> Rows, IReadOnlyList<Problem> FileProblems)
{
    public int ValidCount => Rows.Count(r => r.IsValid);

    public int InvalidCount => Rows.Count - ValidCount;
}

/// <summary>
/// Bulk import of app questions (ADM-T7, story ADM-02). <see cref="PreviewAsync"/> reads and checks every row and saves
/// nothing; when all rows are valid it keeps them for 30 minutes under a key. <see cref="ConfirmAsync"/> saves them all
/// in one transaction, or nothing (D-51).
/// </summary>
public sealed class ImportQuestions(
    IAppDbContext db,
    IQuestionFileReader reader,
    IMemoryCache cache,
    ICurrentUser currentUser,
    IClock clock,
    IOptions<ImportOptions> options)
{
    public const string NoRows = "Admin.Import.NoRows";

    /// <summary>Arg: the row limit.</summary>
    public const string TooManyRows = "Admin.Import.TooManyRows";

    public const string Expired = "Admin.Import.Expired";

    /// <summary>Arg: the topic id.</summary>
    public const string TopicGone = "Admin.Import.TopicGone";

    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(30);

    public async Task<ImportPreview> PreviewAsync(Stream file, string fileName, QuestionStatus status, CancellationToken cancellationToken)
    {
        var maxRows = options.Value.MaxRows;
        var content = reader.Read(file, fileName, maxRows);
        if (content.FileProblem is { } fileProblem)
        {
            return new ImportPreview(null, [], [fileProblem]);
        }

        if (content.Rows.Count == 0)
        {
            return new ImportPreview(null, [], [new Problem(NoRows)]);
        }

        if (content.Rows.Count > maxRows)
        {
            return new ImportPreview(null, [], [new Problem(TooManyRows, null, maxRows)]);
        }

        var topics = new ImportTopicDirectory(await db.Topics
            .Select(t => new ImportTopic(t.Id, t.Name, t.Subject!.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
        var previews = new List<ImportRowPreview>(content.Rows.Count);
        var requests = new List<SaveQuestionRequest>(content.Rows.Count);
        foreach (var row in content.Rows)
        {
            var (request, problems) = QuestionImportMapper.Map(row, topics, status);
            previews.Add(new ImportRowPreview(row.RowNumber, request.Type, TextRules.Clean(row.Text), TextRules.Clean(row.Topic), problems));
            requests.Add(request);
        }

        Guid? key = null;
        if (previews.All(p => p.IsValid))
        {
            key = Guid.NewGuid();
            cache.Set(CacheKey(key.Value), new PendingImport(Actor(), requests), KeepFor);
        }

        return new ImportPreview(key, previews, []);
    }

    /// <summary>Saves the previewed questions. Returns how many were created.</summary>
    public async Task<Result<int>> ConfirmAsync(Guid key, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(CacheKey(key), out PendingImport? pending) || pending is null || pending.ActorUserId != Actor())
        {
            return Result.Fail<int>(new Problem(Expired));
        }

        var topicIds = pending.Requests.Select(r => r.TopicId).Distinct().ToList();
        var existing = await db.Topics.Where(t => topicIds.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (topicIds.Except(existing).FirstOrDefault() is var missing and > 0)
        {
            return Result.Fail<int>(new Problem(TopicGone, null, missing));
        }

        var now = clock.UtcNow;
        await using var transaction = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (var request in pending.Requests)
        {
            var question = new Question
            {
                TopicId = request.TopicId,
                QuestionType = request.Type!.Value,
                Text = TextRules.Clean(request.Text)!,
                Explanation = TextRules.Clean(request.Explanation)!,
                KeyFact = TextRules.Clean(request.KeyFact),
                Hint = TextRules.Clean(request.Hint),
                Difficulty = request.Difficulty,
                SourceNote = TextRules.Clean(request.SourceNote),
                Status = request.Status!.Value,
                CreatedAt = now,
                UpdatedAt = now,
            };
            for (var i = 0; i < request.Options.Count; i++)
            {
                question.Options.Add(new QuestionOption
                {
                    Text = TextRules.Clean(request.Options[i].Text)!,
                    IsCorrect = request.Options[i].IsCorrect,
                    SortOrder = i + 1,
                });
            }

            db.Questions.Add(question);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        cache.Remove(CacheKey(key));
        return Result.Ok(pending.Requests.Count);
    }

    private Guid Actor() => currentUser.UserId ?? throw new InvalidOperationException("Importing needs a signed-in admin.");

    private static string CacheKey(Guid key) => $"question-import:{key:N}";

    private sealed record PendingImport(Guid ActorUserId, IReadOnlyList<SaveQuestionRequest> Requests);
}
