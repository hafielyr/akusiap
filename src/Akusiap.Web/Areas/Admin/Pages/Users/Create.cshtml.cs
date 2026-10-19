using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Users;

/// <summary>
/// <c>/admin/pengguna/baru</c> (USR-02, story ADM-15): create an account with an email, name and roles. The account has
/// no password; the user gets a "set your password" email (D-60). The detail page can send that email again.
/// </summary>
public sealed class CreateModel(
    UserManager<User> users,
    AccountEmails emails,
    IAppDbContext db,
    ICurrentUser currentUser,
    IClock clock,
    Microsoft.Extensions.Localization.IStringLocalizer<Localization.SharedResource> t) : PageModel
{
    public const string Route = "/admin/pengguna/baru";

    [BindProperty]
    public CreateUserInput Input { get; set; } = new();

    public void OnGet() => Input.Roles = [RoleNames.Learner];

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var roles = Input.Roles.Where(r => RoleNames.All.Contains(r)).Distinct().ToList();
        if (roles.Count == 0)
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(CreateUserInput.Roles)}", t["Admin.Users.RoleRequired"]);
        }

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
            PreferredLanguage = Input.PreferredLanguage == Languages.English ? Languages.English : Languages.Indonesian,
            CreatedAt = clock.UtcNow,
        };
        var created = await users.CreateAsync(user);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors.DistinctBy(e => e.Description))
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(CreateUserInput.Email)}", error.Description);
            }

            return Page();
        }

        var added = await users.AddToRolesAsync(user, roles);
        if (!added.Succeeded)
        {
            throw new InvalidOperationException($"Could not add roles: {string.Join("; ", added.Errors.Select(e => e.Code))}");
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = currentUser.UserId!.Value,
            Action = "User.Create",
            EntityName = nameof(User),
            EntityId = user.Id.ToString(),
            Changes = JsonSerializer.Serialize(new { email, roles }),
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);

        TempData["Flash"] = await emails.SendInviteAsync(user, cancellationToken) ? "Admin.Users.Created" : "Admin.Users.CreatedNoEmail";
        return Redirect($"{IndexModel.Route}/{user.Id}");
    }
}

public sealed class CreateUserInput
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

    [Display(Name = "Profile.Language")]
    public string PreferredLanguage { get; set; } = Languages.Indonesian;

    public List<string> Roles { get; set; } = [];
}
