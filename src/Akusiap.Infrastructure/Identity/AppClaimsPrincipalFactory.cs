using System.Security.Claims;
using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Identity;

public static class AppClaimTypes
{
    /// <summary>The user's display name, shown in the header.</summary>
    public const string DisplayName = "akusiap:display_name";
}

/// <summary>Adds the display name to the sign-in cookie so pages don't query the user on every request.</summary>
public sealed class AppClaimsPrincipalFactory(UserManager<User> userManager, RoleManager<Role> roleManager, IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<User, Role>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = await base.GenerateClaimsAsync(user).ConfigureAwait(false);
        identity.AddClaim(new Claim(AppClaimTypes.DisplayName, user.DisplayName));
        return identity;
    }
}
