using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Home;

/// <summary>BRD-09: the landing page ships its meta description, link-preview tags and FAQ structured data.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class HomePageTests(PostgresFixture database)
{
    [Fact]
    public async Task Home_page_has_a_description_link_preview_tags_and_faq_data()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("<meta name=\"description\" content=\"Coba 30 soal gratis", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:title\" content=\"Latihan soal CPNS, BUMN, Kedinasan dan UKMPPD ", html, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"og:locale\" content=\"id_ID\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#main\"", html, StringComparison.Ordinal);

        var json = LdJson().Match(html);
        Assert.True(json.Success, "The page has a JSON-LD block");
        using var faq = JsonDocument.Parse(json.Groups[1].Value);
        Assert.Equal("FAQPage", faq.RootElement.GetProperty("@type").GetString());
        var questions = faq.RootElement.GetProperty("mainEntity");
        Assert.Equal(8, questions.GetArrayLength());
        Assert.Equal("Apakah benar bisa coba gratis?", questions[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Other_pages_fall_back_to_the_site_description()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(new Uri("/harga", UriKind.Relative));

        Assert.Contains("<meta name=\"description\" content=\"Latihan soal untuk seleksi CPNS", html, StringComparison.Ordinal);
        Assert.DoesNotContain("application/ld+json", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("<script type=\"application/ld\\+json\">(.+?)</script>", RegexOptions.Singleline)]
    private static partial Regex LdJson();
}
