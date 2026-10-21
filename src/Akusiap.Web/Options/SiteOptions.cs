namespace Akusiap.Web.Options;

public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>
    /// The public address used in email links, for example <c>https://latihansoal.id</c>. Required outside Development, so
    /// a forged Host header can never point a reset link at another site (D-47).
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
