using System.Security.Claims;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;

namespace Akusiap.Web.Identity;

/// <summary>The signed-in user from the request's cookie claims; a guest when there is no request or no sign-in.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Principal?.Identity?.IsAuthenticated == true
        && Guid.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    public bool IsAdmin => UserId is not null && Principal!.IsInRole(RoleNames.Admin);
}
