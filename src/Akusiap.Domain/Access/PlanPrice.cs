namespace Akusiap.Domain.Access;

public class PlanPrice
{
    public static IReadOnlyList<int> AllowedDurationMonths { get; } = [1, 3, 6, 12];

    public int PlanPriceId { get; set; }

    public int PlanId { get; set; }

    public Plan? Plan { get; set; }

    /// <summary>1, 3, 6 or 12.</summary>
    public int DurationMonths { get; set; }

    /// <summary>Rupiah, no decimals, tax-inclusive.</summary>
    public long PriceIdr { get; set; }

    public bool IsActive { get; set; } = true;
}
