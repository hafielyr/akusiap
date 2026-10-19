using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Identity;

/// <summary>Identity's validation messages in the user's language (keys <c>Identity.*</c> in SharedResource).</summary>
public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer<SharedResource> t) : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => Error(nameof(DefaultError), t["Identity.DefaultError"]);

    // The user name is the email, so both duplicates read as "email already registered".
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), t["Identity.DuplicateEmail", email]);

    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), t["Identity.DuplicateEmail", userName]);

    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), t["Validation.Email"]);

    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), t["Validation.Email"]);

    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), t["Identity.PasswordTooShort", length]);

    public override IdentityError PasswordMismatch() => Error(nameof(PasswordMismatch), t["Account.SignIn.Failed"]);

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
