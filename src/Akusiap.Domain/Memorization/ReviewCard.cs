using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Memorization;

/// <summary>Spaced-repetition state of one question for one user (unique per user and question).</summary>
public class ReviewCard
{
    public const decimal DefaultEaseFactor = 2.50m;

    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public long QuestionId { get; set; }

    public Question? Question { get; set; }

    public ReviewCardState State { get; set; } = ReviewCardState.New;

    public DateTime DueAt { get; set; }

    /// <summary>Two decimals, never negative.</summary>
    public decimal IntervalDays { get; set; }

    /// <summary>SM-2 ease factor, two decimals, above zero.</summary>
    public decimal EaseFactor { get; set; } = DefaultEaseFactor;

    public int Repetitions { get; set; }

    public int Lapses { get; set; }

    public DateTime? LastReviewedAt { get; set; }

    public bool IsBookmarked { get; set; }

    /// <summary>The learner's memory trick.</summary>
    public string? Note { get; set; }

    public ICollection<ReviewLog> Logs { get; } = [];
}
