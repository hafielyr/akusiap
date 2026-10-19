using System.Globalization;
using System.Security.Claims;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace Akusiap.Web.Identity;

/// <summary><c>Authentication:Google</c>: the OAuth client from Google Cloud. Set through env vars or user-secrets only.</summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>Google sign-in (AC-03, D-49). Registered only when the OAuth client is configured.</summary>
public static class GoogleSignIn
{
    public const string Scheme = GoogleDefaults.AuthenticationScheme;

    public static AuthenticationBuilder AddGoogleIfConfigured(this AuthenticationBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>() ?? new GoogleAuthOptions();
        if (!options.IsConfigured)
        {
            return builder;
        }

        return builder.AddGoogle(google =>
        {
            google.ClientId = options.ClientId;
            google.ClientSecret = options.ClientSecret;
            google.SignInScheme = IdentityConstants.ExternalScheme;
            google.CallbackPath = "/signin-google";
        });
    }

    /// <summary>Whether the "Masuk dengan Google" button should be shown.</summary>
    public static async Task<bool> IsEnabledAsync(IAuthenticationSchemeProvider schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        return await schemes.GetSchemeAsync(Scheme) is not null;
    }
}

public enum GoogleAccountOutcome
{
    SignedIn,

    /// <summary>The Google email belongs to a deactivated account.</summary>
    Deactivated,

    /// <summary>Google sent no email address.</summary>
    NoEmail,
}

/// <summary>
/// Finds the account for a Google login: the user already linked to it, else the account with the same email (now
/// linked, and its email marked confirmed), else a new Learner account with a confirmed email and no password.
/// </summary>
public sealed class GoogleAccounts(UserManager<User> users, IClock clock)
{
    public async Task<(GoogleAccountOutcome Outcome, User? User)> FindOrCreateAsync(ExternalLoginInfo login)
    {
        ArgumentNullException.ThrowIfNull(login);
        var user = await users.FindByLoginAsync(login.LoginProvider, login.ProviderKey);
        if (user is not null)
        {
            return user.IsActive ? (GoogleAccountOutcome.SignedIn, user) : (GoogleAccountOutcome.Deactivated, null);
        }

        var email = login.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            return (GoogleAccountOutcome.NoEmail, null);
        }

        user = await users.FindByEmailAsync(email);
        if (user is not null)
        {
            if (!user.IsActive)
            {
                return (GoogleAccountOutcome.Deactivated, null);
            }

            Check(await users.AddLoginAsync(user, login));
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                Check(await users.UpdateAsync(user));
            }

            return (GoogleAccountOutcome.SignedIn, user);
        }

        var name = login.Principal.FindFirstValue(ClaimTypes.Name)?.Trim();
        user = new User
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrEmpty(name) ? email.Split('@')[0] : name[..Math.Min(name.Length, 100)],
            PreferredLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Languages.English ? Languages.English : Languages.Indonesian,
            CreatedAt = clock.UtcNow,
        };
        Check(await users.CreateAsync(user));
        Check(await users.AddToRoleAsync(user, RoleNames.Learner));
        Check(await users.AddLoginAsync(user, login));
        return (GoogleAccountOutcome.SignedIn, user);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Google sign-in could not save the account: {string.Join("; ", result.Errors.Select(e => e.Code))}");
        }
    }
}
