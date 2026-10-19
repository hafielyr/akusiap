using Akusiap.Domain.Access;
using Akusiap.Web.Identity;

namespace Akusiap.IntegrationTests.Access;

/// <summary>ENT-02: what RequireExamAccess returns for each kind of decision.</summary>
public sealed class ExamAccessGuardTests
{
    [Theory]
    [InlineData(AccessLevel.Trial, AccessReason.TrialAvailable, false, ExamAccessOutcome.Allow)]
    [InlineData(AccessLevel.Full, AccessReason.Purchased, true, ExamAccessOutcome.Allow)]
    [InlineData(AccessLevel.Owner, AccessReason.OwnerTakenDown, true, ExamAccessOutcome.Allow)]
    [InlineData(AccessLevel.Shared, AccessReason.SharedLink, false, ExamAccessOutcome.Allow)]
    [InlineData(AccessLevel.None, AccessReason.NotPublished, true, ExamAccessOutcome.NotFound)]
    [InlineData(AccessLevel.None, AccessReason.TakenDown, false, ExamAccessOutcome.NotFound)]
    [InlineData(AccessLevel.None, AccessReason.RegisterToUnlock, false, ExamAccessOutcome.SignIn)]
    [InlineData(AccessLevel.None, AccessReason.NoAccess, false, ExamAccessOutcome.SignIn)]
    [InlineData(AccessLevel.None, AccessReason.NoAccess, true, ExamAccessOutcome.ExamPage)]
    public void Maps_each_decision_to_an_outcome(AccessLevel level, AccessReason reason, bool signedIn, ExamAccessOutcome expected)
    {
        var scope = level == AccessLevel.None ? QuestionScope.None : QuestionScope.All;

        Assert.Equal(expected, ExamAccessGuard.OutcomeFor(new AccessDecision(level, reason, scope), signedIn));
    }

    [Fact]
    public void Missing_exam_is_not_found()
    {
        Assert.Equal(ExamAccessOutcome.NotFound, ExamAccessGuard.OutcomeFor(null, signedIn: true));
    }
}
