using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>Signs out on POST only (antiforgery-protected), so a link or image can't sign someone out.</summary>
public sealed class LogoutModel(SignInManager<User> signIn) : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return LocalRedirect("/");
    }
}
