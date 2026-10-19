using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Identity;

/// <summary>Blocks sign-in for deactivated users: sign-in attempts then return <see cref="SignInResult.NotAllowed"/>.</summary>
public sealed class AppSignInManager(
    UserManager<User> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<User> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<User>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<User> confirmation)
    : SignInManager<User>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsActive && await base.CanSignInAsync(user).ConfigureAwait(false);
    }
}
