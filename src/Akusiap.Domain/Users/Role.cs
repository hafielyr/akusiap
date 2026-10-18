using Microsoft.AspNetCore.Identity;

namespace Akusiap.Domain.Users;

/// <summary>ERD <c>Role</c>. The key is a Guid because Identity needs the same key type as <see cref="User"/> (D-20).</summary>
public class Role : IdentityRole<Guid>
{
    public Role()
    {
    }

    public Role(string name)
        : base(name)
    {
    }
}
