using System.Buffers.Text;
using System.Security.Cryptography;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Akusiap.Application.Sharing;

/// <summary><c>Features</c>: switches for features that ship dark first.</summary>
public sealed class FeatureOptions
{
    public const string SectionName = "Features";

    /// <summary>Shared exam links (SHR-01..05). Off until SHR-05 turns it on.</summary>
    public bool Sharing { get; set; }
}

/// <summary>Share tokens: 16 random bytes as 22 URL-safe characters (SHR-01).</summary>
public static class ShareTokens
{
    public const int Length = 22;

    public static string New() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
}

/// <param name="CanShare">Every question in the exam belongs to the owner (and there is at least one).</param>
public sealed record ShareState(long ExamId, bool IsShared, string? ShareToken, bool CanShare, bool TakenDown);

/// <summary>
/// Sharing a learner's own exam (SHR-01, stories C-06, C-07, D-83): turn the link on or off, or reset it so the old
/// link stops working. Only the owner, only while <see cref="FeatureOptions.Sharing"/> is on, and only when every
/// question in the exam is the owner's own.
/// </summary>
public sealed class ShareExam(IAppDbContext db, ICurrentUser currentUser, IOptions<FeatureOptions> features)
{
    public const string ForeignQuestions = "Share.ForeignQuestions";
    public const string TakenDown = "Share.TakenDown";

    public bool Enabled => features.Value.Sharing;

    public async Task<ShareState?> GetAsync(long examId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return null;
        }

        var userId = UserId();
        return await db.Exams.AsNoTracking()
            .Where(e => e.Id == examId && e.OwnerUserId == userId && e.OwnerKind == OwnerKind.Learner)
            .Select(e => new ShareState(
                e.Id,
                e.IsShared,
                e.ShareToken,
                e.Questions.Any() && e.Questions.All(q => q.Question!.OwnerUserId == userId),
                e.Status == ExamStatus.TakenDown))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Turns the link on, creating the token the first time.</summary>
    public Task<Result<ShareState>> ShareAsync(long examId, CancellationToken cancellationToken) =>
        ChangeAsync(examId, requireShareable: true, (exam, _) =>
        {
            exam.ShareToken ??= ShareTokens.New();
            exam.IsShared = true;
        }, cancellationToken);

    /// <summary>Turns the link off; turning it on again brings back the same link.</summary>
    public Task<Result<ShareState>> StopAsync(long examId, CancellationToken cancellationToken) =>
        ChangeAsync(examId, requireShareable: false, (exam, _) => exam.IsShared = false, cancellationToken);

    /// <summary>A new token, so the old link no longer opens the exam.</summary>
    public Task<Result<ShareState>> ResetAsync(long examId, CancellationToken cancellationToken) =>
        ChangeAsync(examId, requireShareable: false, (exam, _) => exam.ShareToken = ShareTokens.New(), cancellationToken);

    private async Task<Result<ShareState>> ChangeAsync(long examId, bool requireShareable, Action<Exam, Guid> change, CancellationToken cancellationToken)
    {
        var state = await GetAsync(examId, cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            return Result.Fail<ShareState>(Problem.NotFound);
        }

        if (requireShareable && state.TakenDown)
        {
            return Result.Fail<ShareState>(new Problem(TakenDown));
        }

        if (requireShareable && !state.CanShare)
        {
            return Result.Fail<ShareState>(new Problem(ForeignQuestions));
        }

        var exam = await db.Exams.SingleAsync(e => e.Id == examId, cancellationToken).ConfigureAwait(false);
        change(exam, UserId());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok((await GetAsync(examId, cancellationToken).ConfigureAwait(false))!);
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Sharing needs a signed-in user.");
}
