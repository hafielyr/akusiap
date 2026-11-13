namespace Akusiap.Application.Abstractions;

/// <summary>Who is making the current request. Guests have no <see cref="UserId"/>.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    bool IsAdmin { get; }

    /// <summary>Teachers (and admins) can run classes (GAM-01).</summary>
    bool IsTeacher { get; }

    /// <summary>
    /// A guest's browser token (the guest cookie, SES-01), so access rules can see the guest's earlier trials (GST-01).
    /// Null for signed-in users and for guests without the cookie.
    /// </summary>
    Guid? GuestToken => null;
}
