using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Akusiap.Web.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Akusiap.IntegrationTests.Localization;

/// <summary>
/// A key missing from the .resx files renders as its own name ("Home.Fact.Cards"). Merges have dropped keys without
/// breaking the build, so the public pages are rendered in both languages and checked for raw keys and for the
/// framework's English validation messages.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed partial class MissingKeyTests(PostgresFixture database)
{
    public static TheoryData<string> PublicPages =>
        ["/", "/ujian", "/jadwal-tes", "/harga", "/cari", "/akun/masuk", "/akun/daftar", "/akun/lupa-sandi"];

    [Theory]
    [MemberData(nameof(PublicPages))]
    public async Task Public_page_shows_no_raw_resource_keys(string path)
    {
        await using var app = new AppFactory(database);
        var prefixes = KeyPrefixes(app.Services);
        var rawKey = new Regex($@"(?<![\w./#-])(?:{string.Join('|', prefixes.Select(Regex.Escape))})\.[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*(?![\w/])", RegexOptions.None, TimeSpan.FromSeconds(1));

        foreach (var culture in new[] { "id-ID", "en" })
        {
            using var client = app.CreateClient();
            await client.GetStringAsync(new Uri($"/bahasa/{culture}", UriKind.Relative));
            using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            var html = WebUtility.HtmlDecode(Scripts().Replace(await response.Content.ReadAsStringAsync(), string.Empty));
            var keys = rawKey.Matches(html).Select(m => m.Value).Distinct().ToList();
            Assert.True(keys.Count == 0, $"{path} ({culture}) shows raw keys: {string.Join(", ", keys)}");
            if (culture == "id-ID")
            {
                Assert.DoesNotMatch(EnglishValidation(), html);
            }
        }
    }

    private static HashSet<string> KeyPrefixes(IServiceProvider services)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(LocalizationSetup.DefaultCulture);
            var localizer = services.GetRequiredService<IStringLocalizer<Akusiap.Web.Localization.SharedResource>>();
            return [.. localizer.GetAllStrings(includeParentCultures: true).Select(s => s.Name.Split('.')[0])];
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // Inline scripts and styles hold code, not copy; JSON-LD stays because it carries the FAQ text.
    [GeneratedRegex(@"<script(?![^>]*ld\+json)[^>]*>.*?</script>|<style[^>]*>.*?</style>", RegexOptions.Singleline)]
    private static partial Regex Scripts();

    [GeneratedRegex(@"The [\w ]+ field is required|The value '[^']*' is (not valid|invalid)")]
    private static partial Regex EnglishValidation();
}
