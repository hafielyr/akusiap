using System.ComponentModel.DataAnnotations;
using Akusiap.Domain.Users;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary>
/// <c>/learn/profil</c> (AC-04, stories A-03, A-04): display name, phone (+62), preferred language and the
/// "Saya juga mengajar" switch, plus a separate change-password form. Saving refreshes the sign-in cookie so the new
/// name and roles show at once, and sets the culture cookie so the language changes on the next page. Each handler
/// binds and validates only its own form (<c>TryUpdateModelAsync</c>).
/// </summary>
public sealed class ProfileModel(UserManager<User> users, SignInManager<User> signIn, IStringLocalizer<SharedResource> t, Akusiap.Application.Access.GetAllowance getAllowance) : PageModel
{
    public const string Route = "/learn/profil";

    public ProfileInput Input { get; set; } = new();

    public PasswordInput Password { get; set; } = new();

    public string Email { get; private set; } = string.Empty;

    public bool HasPassword { get; private set; } = true;

    /// <summary>Own-content usage for the allowance widget (OWN-05).</summary>
    public Akusiap.Domain.Access.Allowance? Allowance { get; private set; }

    [TempData]
    public string? Flash { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        Input = new ProfileInput
        {
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            PreferredLanguage = user.PreferredLanguage,
            IsTeacher = await users.IsInRoleAsync(user, RoleNames.Teacher),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await TryUpdateModelAsync(Input, nameof(Input));
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(Input.PhoneNumber))
        {
            if (PhoneNumbers.TryNormalize(Input.PhoneNumber, out var normalized))
            {
                phone = normalized;
            }
            else
            {
                ModelState.AddModelError($"{nameof(Input)}.{nameof(ProfileInput.PhoneNumber)}", t["Validation.Phone"]);
            }
        }

        if (Input.PreferredLanguage is not (Languages.Indonesian or Languages.English))
        {
            ModelState.AddModelError($"{nameof(Input)}.{nameof(ProfileInput.PreferredLanguage)}", t["Validation.Required", t["Profile.Language"]]);
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return Page();
        }

        user.DisplayName = Input.DisplayName.Trim();
        user.PhoneNumber = phone;
        user.PreferredLanguage = Input.PreferredLanguage;
        Check(await users.UpdateAsync(user));

        var isTeacher = await users.IsInRoleAsync(user, RoleNames.Teacher);
        if (Input.IsTeacher && !isTeacher)
        {
            Check(await users.AddToRoleAsync(user, RoleNames.Teacher));
        }
        else if (!Input.IsTeacher && isTeacher)
        {
            Check(await users.RemoveFromRoleAsync(user, RoleNames.Teacher));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await signIn.RefreshSignInAsync(user);
        LocalizationSetup.SetCultureCookie(Response, LocalizationSetup.CultureFor(user.PreferredLanguage));
        Flash = "Profile.Saved";
        return Redirect(Route);
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await TryUpdateModelAsync(Password, nameof(Password));

        var hasPassword = await users.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrEmpty(Password.Current))
        {
            ModelState.AddModelError($"{nameof(Password)}.{nameof(PasswordInput.Current)}", t["Validation.Required", t["Profile.CurrentPassword"]]);
        }

        if (!ModelState.IsValid)
        {
            await ReloadAsync(user);
            return Page();
        }

        var result = hasPassword
            ? await users.ChangePasswordAsync(user, Password.Current!, Password.New)
            : await users.AddPasswordAsync(user, Password.New);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors.DistinctBy(e => e.Description))
            {
                var field = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? nameof(PasswordInput.Current) : nameof(PasswordInput.New);
                var message = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? t["Profile.WrongPassword"].Value : error.Description;
                ModelState.AddModelError($"{nameof(Password)}.{field}", message);
            }

            await ReloadAsync(user);
            return Page();
        }

        cancellationToken.ThrowIfCancellationRequested();
        await signIn.RefreshSignInAsync(user);
        Flash = "Profile.PasswordChanged";
        return Redirect(Route);
    }

    private async Task LoadAsync(User user)
    {
        Email = user.Email ?? string.Empty;
        HasPassword = await users.HasPasswordAsync(user);
        Allowance = await getAllowance.HandleAsync(HttpContext.RequestAborted);
    }

    /// <summary>Shows the profile form with the saved values again after a password error.</summary>
    private async Task ReloadAsync(User user)
    {
        await LoadAsync(user);
        Input = new ProfileInput
        {
            DisplayName = user.DisplayName,
            PhoneNumber = user.PhoneNumber,
            PreferredLanguage = user.PreferredLanguage,
            IsTeacher = await users.IsInRoleAsync(user, RoleNames.Teacher),
        };
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Could not save the profile: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}

public sealed class ProfileInput
{
    [Display(Name = "Account.DisplayName")]
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(100, ErrorMessage = "Validation.StringLength")]
    public string DisplayName { get; set; } = string.Empty;

    [Display(Name = "Profile.Phone")]
    [StringLength(32, ErrorMessage = "Validation.StringLength")]
    public string? PhoneNumber { get; set; }

    [Display(Name = "Profile.Language")]
    public string PreferredLanguage { get; set; } = Languages.Indonesian;

    [Display(Name = "Profile.Teacher")]
    public bool IsTeacher { get; set; }
}

public sealed class PasswordInput
{
    [Display(Name = "Profile.CurrentPassword")]
    [DataType(DataType.Password)]
    public string? Current { get; set; }

    [Display(Name = "Profile.NewPassword")]
    [Required(ErrorMessage = "Validation.Required")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Validation.PasswordLength")]
    [DataType(DataType.Password)]
    public string New { get; set; } = string.Empty;
}
