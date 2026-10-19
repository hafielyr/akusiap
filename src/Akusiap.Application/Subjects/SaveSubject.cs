using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Subjects;

/// <summary>Create (Id null) or edit a subject. Names are unique, ignoring case.</summary>
public sealed record SaveSubjectRequest(long? Id, string? Name, string? Description);

public static class SubjectRules
{
    public const string NameField = "Name";
    public const string DescriptionField = "Description";
    public const string NameLabel = "Admin.Subject.Name";
    public const string DescriptionLabel = "Admin.Subject.Description";
    public const string NameTaken = "Admin.Subjects.NameTaken";

    /// <summary>Args: topic count, exam count.</summary>
    public const string InUse = "Admin.Subjects.InUse";

    public static List<Problem> Validate(SaveSubjectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<Problem>();
        TextRules.RequireName(errors, NameField, NameLabel, request.Name);
        TextRules.LimitDescription(errors, DescriptionField, DescriptionLabel, request.Description);
        return errors;
    }
}

public sealed class SaveSubject(IAppDbContext db)
{
    public async Task<Result<long>> HandleAsync(SaveSubjectRequest request, CancellationToken cancellationToken)
    {
        var errors = SubjectRules.Validate(request);
        if (errors.Count > 0)
        {
            return Result.Fail<long>(errors);
        }

        var name = TextRules.Clean(request.Name)!;
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
        var lowered = name.ToLower();
        if (await db.Subjects.AnyAsync(s => s.Name.ToLower() == lowered && s.Id != request.Id, cancellationToken).ConfigureAwait(false))
#pragma warning restore CA1304, CA1311, CA1862
        {
            return Result.Fail<long>(new Problem(SubjectRules.NameTaken, SubjectRules.NameField));
        }

        Subject? subject;
        if (request.Id is { } id)
        {
            subject = await db.Subjects.SingleOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
            if (subject is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }
        }
        else
        {
            subject = new Subject();
            db.Subjects.Add(subject);
        }

        subject.Name = name;
        subject.Description = TextRules.Clean(request.Description);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another admin saved the same name at the same moment (unique index on subjects.name).
            return Result.Fail<long>(new Problem(SubjectRules.NameTaken, SubjectRules.NameField));
        }

        return Result.Ok(subject.Id);
    }
}
