namespace Akusiap.Web.Identity;

/// <summary>
/// Lets a background job run application code as one user (PLN-05): set <see cref="UserId"/> in a fresh DI scope and
/// <see cref="HttpCurrentUser"/> reports that user, without admin or teacher roles. Never set during a web request.
/// </summary>
public sealed class ActingUser
{
    public Guid? UserId { get; set; }
}
