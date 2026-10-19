using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.TestSupport.Web;

public static class AppFactoryExtensions
{
    /// <summary>Roles, plans and the default preset (idempotent, no sample content). Safe to call in the shared database.</summary>
    public static async Task SeedReferenceDataAsync(this WebApplicationFactory<Program> app)
    {
        ArgumentNullException.ThrowIfNull(app);
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(includeSampleContent: false, CancellationToken.None);
    }

    /// <summary>Creates <paramref name="user"/> through Identity so the password is hashed, then adds the roles.</summary>
    public static async Task<User> CreateUserAsync(
        this WebApplicationFactory<Program> app, User user, string password, params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(app);
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Check(await users.CreateAsync(user, password));
        if (roles.Length > 0)
        {
            Check(await users.AddToRolesAsync(user, roles));
        }

        return user;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
