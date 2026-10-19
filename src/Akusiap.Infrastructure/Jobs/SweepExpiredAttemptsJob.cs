using Akusiap.Application.Abstractions;
using Akusiap.Application.Sessions;
using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Infrastructure.Jobs;

/// <summary>
/// Every 5 minutes (SES-06): finds InProgress attempts whose deadline (StartedAt + the snapshot's time limit) has passed
/// and closes them as Expired through <see cref="ExpireAttempts"/>. At most 500 per run.
/// </summary>
public sealed class SweepExpiredAttemptsJob(AppDbContext db, ExpireAttempts expire, IClock clock)
{
    public const string Id = "sweep-expired-attempts";
    public const string Cron = "*/5 * * * *";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var ids = await db.Database
            .SqlQuery<long>($"""
                SELECT id AS "Value" FROM attempts
                WHERE status = 'InProgress'
                  AND jsonb_typeof(rules_snapshot -> 'timeLimitMinutes') = 'number'
                  AND started_at + make_interval(mins => (rules_snapshot ->> 'timeLimitMinutes')::int) < {now}
                ORDER BY id
                LIMIT 500
                """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return await expire.HandleAsync(ids, cancellationToken).ConfigureAwait(false);
    }
}
