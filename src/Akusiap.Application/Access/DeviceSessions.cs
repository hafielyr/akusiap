using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>A device on the profile page.</summary>
public sealed record DeviceRow(long Id, string Label, DateTime LastSignInAt, bool IsCurrent);

/// <summary>The user's signed-in devices and how many their subscription allows (null: no limit).</summary>
public sealed record DeviceList(IReadOnlyList<DeviceRow> Devices, int? Allowed);

/// <summary>
/// The device limit (PAY-13, D-141). Every sign-in registers the browser's device cookie for the user. While the user has
/// an active subscription, at most 1 + its extra devices stay signed in: the newest sign-in wins and the devices that
/// signed in longest ago are pushed out (signed out on their next request). Users without an active subscription have
/// no limit.
/// </summary>
public sealed class DeviceSessions(IAppDbContext db, IClock clock)
{
    /// <summary>Registers or revives the device, pushes out devices over the limit and returns the device id.</summary>
    public async Task<long> SignedInAsync(Guid userId, Guid deviceKey, string label, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var device = await db.UserDevices.SingleOrDefaultAsync(d => d.UserId == userId && d.DeviceKey == deviceKey, cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            device = new UserDevice { UserId = userId, DeviceKey = deviceKey, CreatedAt = now };
            db.UserDevices.Add(device);
        }

        device.Label = label.Length > UserDevice.LabelMaxLength ? label[..UserDevice.LabelMaxLength] : label;
        device.LastSignInAt = now;
        device.RevokedAt = null;
        device.PushedOut = false;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (await AllowedAsync(userId, cancellationToken).ConfigureAwait(false) is { } allowed)
        {
            var others = await db.UserDevices
                .Where(d => d.UserId == userId && d.Id != device.Id && d.RevokedAt == null)
                .OrderByDescending(d => d.LastSignInAt).ThenByDescending(d => d.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var old in others.Skip(allowed - 1))
            {
                old.RevokedAt = now;
                old.PushedOut = true;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return device.Id;
    }

    /// <summary>Whether the device may stay signed in.</summary>
    public Task<bool> IsActiveAsync(Guid userId, long deviceId, CancellationToken cancellationToken) =>
        db.UserDevices.AnyAsync(d => d.Id == deviceId && d.UserId == userId && d.RevokedAt == null, cancellationToken);

    public async Task SignedOutAsync(Guid userId, long deviceId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await db.UserDevices
            .Where(d => d.Id == deviceId && d.UserId == userId && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevokedAt, now), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The devices still signed in, newest first, and the limit.</summary>
    public async Task<DeviceList> ListAsync(Guid userId, long? currentDeviceId, CancellationToken cancellationToken)
    {
        var rows = await db.UserDevices
            .Where(d => d.UserId == userId && d.RevokedAt == null)
            .OrderByDescending(d => d.LastSignInAt).ThenByDescending(d => d.Id)
            .Select(d => new DeviceRow(d.Id, d.Label, d.LastSignInAt, d.Id == currentDeviceId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new DeviceList(rows, await AllowedAsync(userId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>1 + the most extra devices among the user's active subscriptions; null without one.</summary>
    public async Task<int?> AllowedAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var extras = await db.Subscriptions
            .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now)
            .Select(s => s.ExtraDevices)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return extras.Count == 0 ? null : DeviceLimits.Included + extras.Max();
    }
}
