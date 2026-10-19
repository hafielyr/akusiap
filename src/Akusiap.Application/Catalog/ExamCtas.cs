using Akusiap.Domain.Access;

namespace Akusiap.Application.Catalog;

/// <summary>A button on the exam page (CAT-02).</summary>
public enum ExamCta
{
    /// <summary>"Mulai latihan": the viewer can use every question.</summary>
    StartPractice,

    /// <summary>"Coba {n} soal gratis": the trial set only.</summary>
    StartTrial,

    /// <summary>"Beli ujian": single-exam checkout.</summary>
    Buy,

    /// <summary>"Berlangganan": the plans page.</summary>
    Subscribe,

    /// <summary>"Daftar gratis untuk membuka": a guest on a free exam (D-05).</summary>
    RegisterToUnlock,
}

/// <summary>Maps an <see cref="AccessDecision"/> to the exam page's buttons, most important first (CAT-02, D-43).</summary>
public static class ExamCtas
{
    public static IReadOnlyList<ExamCta> For(AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision switch
        {
            { Level: AccessLevel.Trial } => [ExamCta.StartTrial, ExamCta.Buy, ExamCta.Subscribe],
            { CanStart: true } => [ExamCta.StartPractice],
            { Reason: AccessReason.RegisterToUnlock } => [ExamCta.RegisterToUnlock],
            { Level: AccessLevel.None, Reason: AccessReason.NoAccess } => [ExamCta.Buy, ExamCta.Subscribe],
            _ => [],
        };
    }

    /// <summary>Unpublished and taken-down exams are not revealed: the page answers 404 (as ExamAccessGuard does).</summary>
    public static bool IsVisible(AccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.Level != AccessLevel.None
            || decision.Reason is not (AccessReason.NotPublished or AccessReason.TakenDown);
    }
}
