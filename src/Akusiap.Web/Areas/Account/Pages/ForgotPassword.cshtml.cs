using System.ComponentModel.DataAnnotations;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>
/// Asks for a reset link (AC-02). The answer is the same whether or not the email is registered, so the page never
/// reveals who has an account; deactivated accounts get no email.
/// </summary>
public sealed class ForgotPasswordModel(UserManager<User> users, AccountEmails emails) : PageModel
{
    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool Sent { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is { IsActive: true })
        {
            await emails.SendPasswordResetAsync(user, cancellationToken);
        }

        Sent = true;
        return Page();
    }
}

public sealed class ForgotPasswordInput
{
    [Display(Name = "Account.Email")]
    [Required(ErrorMessage = "Validation.Required")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    [StringLength(256, ErrorMessage = "Validation.StringLength")]
    public string Email { get; set; } = string.Empty;
}
