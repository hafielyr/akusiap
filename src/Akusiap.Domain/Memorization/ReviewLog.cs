namespace Akusiap.Domain.Memorization;

public class ReviewLog
{
    public long Id { get; set; }

    public long ReviewCardId { get; set; }

    public ReviewCard? ReviewCard { get; set; }

    public ReviewRating Rating { get; set; }

    public decimal IntervalBeforeDays { get; set; }

    public decimal IntervalAfterDays { get; set; }

    public DateTime ReviewedAt { get; set; }
}
