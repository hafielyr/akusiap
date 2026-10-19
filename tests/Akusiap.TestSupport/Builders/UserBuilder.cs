using Akusiap.Domain.Users;

namespace Akusiap.TestSupport.Builders;

/// <summary>Builds an active, confirmed user with a unique email. Roles are added by the code under test or by seed data.</summary>
public sealed class UserBuilder
{
    private string _email = $"user-{TestData.Unique()}@example.com";
    private string _displayName = "Pengguna Uji";
    private string _language = Languages.Indonesian;
    private bool _emailConfirmed = true;
    private DateTime? _deactivatedAt;

    public UserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public UserBuilder Named(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    public UserBuilder English()
    {
        _language = Languages.English;
        return this;
    }

    public UserBuilder Unconfirmed()
    {
        _emailConfirmed = false;
        return this;
    }

    public UserBuilder Deactivated()
    {
        _deactivatedAt = TestData.Now;
        return this;
    }

    public User Build() => new()
    {
        Id = Guid.NewGuid(),
        UserName = _email,
        NormalizedUserName = _email.ToUpperInvariant(),
        Email = _email,
        NormalizedEmail = _email.ToUpperInvariant(),
        EmailConfirmed = _emailConfirmed,
        SecurityStamp = Guid.NewGuid().ToString("N"),
        DisplayName = _displayName,
        PreferredLanguage = _language,
        IsActive = _deactivatedAt is null,
        DeactivatedAt = _deactivatedAt,
        CreatedAt = TestData.Now,
    };

    public static implicit operator User(UserBuilder builder) => (builder ?? throw new ArgumentNullException(nameof(builder))).Build();
}
