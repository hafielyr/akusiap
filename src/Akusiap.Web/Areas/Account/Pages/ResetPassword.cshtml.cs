using System.ComponentModel.DataAnnotations;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>
/// The link from the reset email (AC-02). A successful reset also confirms the email (the link proves the inbox) and
/// clears a lockout, then sends the user to sign in.
/// </summary>
public sealed class ResetPasswordModel(UserManager<User> users) : PageModel
{
    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public bool LinkInvalid { get; private set; }

    public IActionResult OnGet(string? email, string? code)
    {
        if (string.IsNullOrWhiteSpace(email) || AccountEmails.Decode(code) is null)
        {
            LinkInvalid = true;
            return Page();
        }

        Input = new ResetPasswordInput { Email = email, Code = code! };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var token = AccountEmails.Decode(Input.Code);
        if (token is null)
        {
            LinkInvalid = true;
            return Page();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null)
        {
            LinkInvalid = true;
            return Page();
        }

        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                LinkInvalid = true;
                return Page();
            }

            foreach (var error in result.Errors.DistinctBy(e => e.Description))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(ResetPasswordInput.Password)}", error.Description);
            }

            return Page();
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return LocalRedirect($"{AccountRoutes.SignIn}?reset=true");
    }
}

public sealed class ResetPasswordInput
{
    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Account.Reset.NewPassword")]
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Validation.PasswordLength")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
