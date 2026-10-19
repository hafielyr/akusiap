using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>
/// Email registration (A-01). New users get the Learner role and a confirmation email (AC-02), are signed in and land on
/// the catalog (or the local ReturnUrl).
/// </summary>
public sealed class RegisterModel(UserManager<User> users, SignInManager<User> signIn, IClock clock, AccountEmails emails, ClaimGuestAttempts claimGuestAttempts) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    /// <summary>Where to go after registering, such as the exam page that asked (CAT-02). Local URLs only.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = Input.Email.Trim();
        var user = new User
        {
            UserName = email,
            Email = email,
            DisplayName = Input.DisplayName.Trim(),
            PreferredLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == Languages.English
                ? Languages.English
                : Languages.Indonesian,
            CreatedAt = clock.UtcNow,
        };

        var created = await users.CreateAsync(user, Input.Password);
        if (!created.Succeeded)
        {
            AddErrors(created);
            return Page();
        }

        var role = await users.AddToRoleAsync(user, RoleNames.Learner);
        if (!role.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not give the new user the {RoleNames.Learner} role: {string.Join("; ", role.Errors.Select(e => e.Description))}");
        }

        // Guest attempts from this browser (shared exams, trials) move into the new account (SHR-04).
        if (GuestTokenCookie.Read(Request) is { } guestToken)
        {
            await claimGuestAttempts.HandleAsync(user.Id, guestToken, cancellationToken);
        }

        // A failed send is logged; the user can ask for the email again from /learn.
        await emails.SendConfirmationAsync(user, cancellationToken);
        await signIn.SignInAsync(user, isPersistent: true);
        return LocalRedirect(SafeReturnUrl);
    }

    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : AccountRoutes.AfterSignIn;

    private void AddErrors(IdentityResult result)
    {
        // DuplicateUserName and DuplicateEmail both fire for one email; show the message once.
        foreach (var error in result.Errors.DistinctBy(e => e.Description))
        {
            var field = error.Code switch
            {
                _ when error.Code.StartsWith("Duplicate", StringComparison.Ordinal) || error.Code.StartsWith("Invalid", StringComparison.Ordinal)
                    => $"{nameof(Input)}.{nameof(RegisterInput.Email)}",
                _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => $"{nameof(Input)}.{nameof(RegisterInput.Password)}",
                _ => string.Empty,
            };
            ModelState.AddModelError(field, error.Description);
        }
    }
}

public sealed class RegisterInput
{
    [Display(Name = "Account.Email")]
    [Required(ErrorMessage = "Validation.Required")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [StringLength(256, ErrorMessage = "Validation.StringLength")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Account.DisplayName")]
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Account.Password")]
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Validation.PasswordLength")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
