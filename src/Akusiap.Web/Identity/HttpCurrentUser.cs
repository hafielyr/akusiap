using System.Security.Claims;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;

namespace Akusiap.Web.Identity;

/// <summary>
/// The signed-in user from the request's cookie claims; a guest when there is no request or no sign-in. A background
/// job can act as one user through <see cref="ActingUser"/> (no roles).
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor, ActingUser acting) : ICurrentUser
{
    private ClaimsPrincipal? Principal => acting.UserId is null ? accessor.HttpContext?.User : null;

    public Guid? UserId => acting.UserId ??
        (Principal?.Identity?.IsAuthenticated == true
        && Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null);

    public bool IsAdmin => Principal is not null && UserId is not null && Principal.IsInRole(RoleNames.Admin);

    public Guid? GuestToken => UserId is null && accessor.HttpContext is { } context ? Sessions.GuestTokenCookie.Read(context.Request) : null;

    public bool IsTeacher => Principal is not null && UserId is not null && (Principal.IsInRole(RoleNames.Teacher) || Principal.IsInRole(RoleNames.Admin));
}
