namespace Akusiap.Domain.Access;

public class PlanPrice
{
    public static IReadOnlyList<int> AllowedDurationMonths { get; } = [1, 3, 6, 12];

    public long Id { get; set; }

    public long PlanId { get; set; }

    public Plan? Plan { get; set; }

    /// <summary>1, 3, 6 or 12; one price per duration per plan.</summary>
    public int DurationMonths { get; set; }

    /// <summary>Rupiah, no decimals, tax-inclusive.</summary>
    public long PriceIdr { get; set; }

    public bool IsActive { get; set; } = true;
}
