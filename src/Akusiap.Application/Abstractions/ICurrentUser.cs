namespace Akusiap.Application.Abstractions;

/// <summary>Who is making the current request. Guests have no <see cref="UserId"/>.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAdmin { get; }

    /// <summary>Teachers (and admins) can run classes (GAM-01).</summary>
    bool IsTeacher { get; }
}
