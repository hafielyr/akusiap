using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Topics;

/// <summary>Create (Id null) or edit a topic of a subject. Names are unique within the subject, ignoring case.</summary>
public sealed record SaveTopicRequest(long? Id, long SubjectId, string? Name, string? Description);

public static class TopicRules
{
    public const string NameField = "Name";
    public const string DescriptionField = "Description";
    public const string SubjectField = "SubjectId";
    public const string NameLabel = "Admin.Topic.Name";
    public const string DescriptionLabel = "Admin.Topic.Description";
    public const string NameTaken = "Admin.Topics.NameTaken";
    public const string SubjectMissing = "Admin.Topics.SubjectMissing";

    /// <summary>Arg: question count.</summary>
    public const string InUse = "Admin.Topics.InUse";

    public static List<Problem> Validate(SaveTopicRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<Problem>();
        TextRules.RequireName(errors, NameField, NameLabel, request.Name);
        TextRules.LimitDescription(errors, DescriptionField, DescriptionLabel, request.Description);
        if (request.SubjectId <= 0)
        {
            errors.Add(new Problem(SubjectMissing, SubjectField));
        }

        return errors;
    }
}

public sealed class SaveTopic(IAppDbContext db)
{
    public async Task<Result<long>> HandleAsync(SaveTopicRequest request, CancellationToken cancellationToken)
    {
        var errors = TopicRules.Validate(request);
        if (errors.Count > 0)
        {
            return Result.Fail<long>(errors);
        }

        if (!await db.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(TopicRules.SubjectMissing, TopicRules.SubjectField));
        }

        var name = TextRules.Clean(request.Name)!;
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
        var lowered = name.ToLower();
        var taken = await db.Topics
            .AnyAsync(t => t.SubjectId == request.SubjectId && t.Name.ToLower() == lowered && t.Id != request.Id, cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore CA1304, CA1311, CA1862
        if (taken)
        {
            return Result.Fail<long>(new Problem(TopicRules.NameTaken, TopicRules.NameField));
        }

        Topic? topic;
        if (request.Id is { } id)
        {
            topic = await db.Topics.SingleOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
            if (topic is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }
        }
        else
        {
            topic = new Topic();
            db.Topics.Add(topic);
        }

        topic.SubjectId = request.SubjectId;
        topic.Name = name;
        topic.Description = TextRules.Clean(request.Description);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Saved at the same moment by someone else (unique index on topics(subject_id, name)).
            return Result.Fail<long>(new Problem(TopicRules.NameTaken, TopicRules.NameField));
        }

        return Result.Ok(topic.Id);
    }
}
