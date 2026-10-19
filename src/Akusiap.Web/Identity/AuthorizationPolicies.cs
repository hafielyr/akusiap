using Akusiap.Domain.Users;

namespace Akusiap.Web.Identity;

/// <summary>Named authorization policies (AC-06).</summary>
public static class AuthorizationPolicies
{
    /// <summary>Signed in with the Admin role. Applied to every page in the Admin area.</summary>
    public const string AdminOnly = nameof(AdminOnly);

    /// <summary>Any signed-in user. Applied to every page in the Learn area; a page may opt out with <c>[AllowAnonymous]</c>.</summary>
    public const string SignedIn = nameof(SignedIn);

    public const string AdminArea = "Admin";
    public const string LearnArea = "Learn";

    public static IServiceCollection AddAppAuthorization(this IServiceCollection services) =>
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole(RoleNames.Admin))
            .AddPolicy(SignedIn, policy => policy.RequireAuthenticatedUser())
            .Services;
}
