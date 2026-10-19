using Akusiap.Domain.Payments;

namespace Akusiap.Application.Payments;

/// <summary>One way to pay at checkout: QRIS, a VA bank or an e-wallet (ID-18).</summary>
public sealed class PaymentChannelSetting
{
    /// <summary>Stable id used in forms, for example <c>QRIS</c>, <c>VA-BCA</c>, <c>EWALLET-OVO</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public PaymentMethod Method { get; set; }

    /// <summary>The bank or wallet passed to the gateway; null for QRIS.</summary>
    public string? Channel { get; set; }

    /// <summary>Shown to the learner.</summary>
    public string Label { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>Hide the channel for smaller amounts (per-transaction fees). 0 = no minimum.</summary>
    public long MinAmountIdr { get; set; }
}

/// <summary>
/// <c>Payments:Channels</c> (PAY-03, D-58). The defaults are every channel enabled with no minimum; the owner sets the
/// real list and minimums in configuration (an admin page can replace this later).
/// </summary>
public sealed class PaymentChannelOptions
{
    public const string SectionName = "Payments";

    public List<PaymentChannelSetting> Channels { get; set; } = [];

    public static IReadOnlyList<PaymentChannelSetting> Defaults { get; } =
    [
        new() { Code = "QRIS", Method = PaymentMethod.QRIS, Label = "QRIS" },
        new() { Code = "VA-BCA", Method = PaymentMethod.VirtualAccount, Channel = "BCA", Label = "BCA Virtual Account" },
        new() { Code = "VA-BNI", Method = PaymentMethod.VirtualAccount, Channel = "BNI", Label = "BNI Virtual Account" },
        new() { Code = "VA-BRI", Method = PaymentMethod.VirtualAccount, Channel = "BRI", Label = "BRI Virtual Account" },
        new() { Code = "VA-MANDIRI", Method = PaymentMethod.VirtualAccount, Channel = "MANDIRI", Label = "Mandiri Virtual Account" },
        new() { Code = "EWALLET-OVO", Method = PaymentMethod.EWallet, Channel = "OVO", Label = "OVO" },
        new() { Code = "EWALLET-DANA", Method = PaymentMethod.EWallet, Channel = "DANA", Label = "DANA" },
        new() { Code = "EWALLET-SHOPEEPAY", Method = PaymentMethod.EWallet, Channel = "SHOPEEPAY", Label = "ShopeePay" },
    ];

    /// <summary>The channels to offer for <paramref name="amountIdr"/>: enabled, at or above their minimum, QRIS first, then VA, then e-wallets.</summary>
    public IReadOnlyList<PaymentChannelSetting> AvailableFor(long amountIdr)
    {
        var list = Channels.Count > 0 ? Channels : Defaults;
        return [.. list
            .Select((c, i) => (Channel: c, Index: i))
            .Where(x => x.Channel.Enabled && amountIdr >= x.Channel.MinAmountIdr)
            .OrderBy(x => Rank(x.Channel.Method))
            .ThenBy(x => x.Index)
            .Select(x => x.Channel)];
    }

    private static int Rank(PaymentMethod method) => method switch
    {
        PaymentMethod.QRIS => 0,
        PaymentMethod.VirtualAccount => 1,
        PaymentMethod.EWallet => 2,
        _ => 3,
    };
}
