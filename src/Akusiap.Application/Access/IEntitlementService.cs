using Akusiap.Domain.Access;

namespace Akusiap.Application.Access;

/// <summary>An exam's access facts and the decision for the current viewer.</summary>
public sealed record EntitlementResult(ExamAccessFacts Exam, AccessDecision Decision);

/// <summary>
/// The only way pages and handlers ask about exam access (AGENTS.md). Never query purchases or subscriptions directly.
/// </summary>
public interface IEntitlementService
{
    /// <summary>The decision for the current viewer, or null when the exam doesn't exist.</summary>
    Task<EntitlementResult?> GetAsync(long examId, string? shareToken, CancellationToken cancellationToken);

    /// <summary>The current viewer's facts, loaded once per request (one database round-trip, none for guests).</summary>
    Task<ViewerContext> GetViewerAsync(CancellationToken cancellationToken);
}
