using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Users;

public enum SubscriptionFilter
{
    Any,

    /// <summary>Has a subscription that is Active and not yet ended.</summary>
    Active,

    /// <summary>Has no running subscription.</summary>
    None,
}

/// <summary>All filters are optional and combine. <paramref name="Search"/> matches email or display name, ignoring case.</summary>
public sealed record UserFilter(string? Search = null, string? Role = null, bool? IsActive = null, SubscriptionFilter Subscription = SubscriptionFilter.Any, int Page = 1);

public sealed record UserRow(Guid Id, string Email, string DisplayName, bool IsActive, IReadOnlyList<string> Roles, bool HasActiveSubscription, DateTime CreatedAt);

public sealed record UserPage(IReadOnlyList<UserRow> Items, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>The admin user list (USR-01, story ADM-14): newest accounts first, 20 per page.</summary>
public sealed class ListUsers(IAppDbContext db, IClock clock)
{
    public const int PageSize = 20;

    public async Task<UserPage> HandleAsync(UserFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var now = clock.UtcNow;
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower().
            var term = filter.Search.Trim().ToLower();
            query = query.Where(u => u.Email!.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var role = filter.Role;
            query = query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(u => u.IsActive == active);
        }

        if (filter.Subscription != SubscriptionFilter.Any)
        {
            var wanted = filter.Subscription == SubscriptionFilter.Active;
            query = query.Where(u => db.Subscriptions.Any(s => s.UserId == u.Id && s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now) == wanted);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.IsActive,
                u.CreatedAt,
                Roles = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToList(),
                HasActiveSubscription = db.Subscriptions.Any(s => s.UserId == u.Id && s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UserPage(
            [.. rows.Select(r => new UserRow(r.Id, r.Email ?? string.Empty, r.DisplayName, r.IsActive, [.. r.Roles.Order(StringComparer.Ordinal)], r.HasActiveSubscription, r.CreatedAt))],
            page,
            PageSize,
            total);
    }
}

/// <summary>One user for the admin detail page: profile, roles, access (ENT-03) and how many attempts they made.</summary>
public sealed record UserDetail(
    Guid Id,
    string Email,
    bool EmailConfirmed,
    string DisplayName,
    string? PhoneNumber,
    string PreferredLanguage,
    bool IsActive,
    DateTime? DeactivatedAt,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles,
    int AttemptCount,
    UserAccess Access);

public sealed class GetUserDetail(IAppDbContext db, GetUserAccess userAccess)
{
    public async Task<UserDetail?> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.EmailConfirmed,
                u.DisplayName,
                u.PhoneNumber,
                u.PreferredLanguage,
                u.IsActive,
                u.DeactivatedAt,
                u.CreatedAt,
                Roles = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToList(),
                Attempts = db.Attempts.Count(a => a.UserId == u.Id),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var access = await userAccess.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        return new UserDetail(
            user.Id, user.Email ?? string.Empty, user.EmailConfirmed, user.DisplayName, user.PhoneNumber, user.PreferredLanguage, user.IsActive,
            user.DeactivatedAt, user.CreatedAt, [.. user.Roles.Order(StringComparer.Ordinal)], user.Attempts, access!);
    }
}
