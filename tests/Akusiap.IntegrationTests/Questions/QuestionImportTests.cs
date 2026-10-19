using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Questions;

/// <summary>ADM-T7: the import page checks every row first and saves all rows or none.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class QuestionImportTests(PostgresFixture database)
{
    private const string Page = "/admin/soal/impor";

    [Fact]
    public async Task A_file_with_one_bad_row_saves_nothing_and_shows_the_row_number_and_error()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topic = await TopicAsync();
        var csv = Csv(
            Row("SingleChoice", $"Soal satu {topic}", "A", topic),
            Row("SingleChoice", $"Soal dua {topic}", "Z", topic),
            Row("SingleChoice", $"Soal tiga {topic}", "B", topic));

        using var response = await UploadAsync(admin, csv, "soal.csv");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("3 baris dibaca: 2 siap, 1 bermasalah.", html, StringComparison.Ordinal);
        Assert.Contains("data-row=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("Baris 3", html, StringComparison.Ordinal);
        Assert.Contains("Jawaban benar \"Z\" harus berupa huruf A–H.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"confirm-form\"", html, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(topic));
    }

    [Fact]
    public async Task A_valid_file_of_500_rows_imports_in_one_go_under_10_seconds()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topic = await TopicAsync();
        var csv = Csv([.. Enumerable.Range(1, 500).Select(i => Row(i % 3 == 0 ? "MultiSelect" : "SingleChoice", $"Soal {i} {topic}", i % 3 == 0 ? "A,C" : "B", topic))]);

        var timer = Stopwatch.StartNew();
        using var preview = await UploadAsync(admin, csv, "soal.csv", status: "Draft");
        var html = await preview.Content.ReadAsStringAsync();
        var key = KeyPattern().Match(html).Groups[1].Value;
        using var confirmed = await admin.PostFormAsync(Page, new Dictionary<string, string> { ["key"] = key }, $"{Page}?handler=Confirm");
        timer.Stop();

        Assert.Contains("500 baris dibaca: 500 siap, 0 bermasalah.", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Import took {timer.Elapsed}");
        Assert.Equal(500, await CountAsync(topic));
        Assert.Contains("500 soal berhasil diimpor.", WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri("/admin/soal", UriKind.Relative))), StringComparison.Ordinal);

        await using var db = database.CreateContext();
        var multi = await db.Questions.Include(q => q.Options).FirstAsync(q => q.Text == $"Soal 3 {topic}");
        Assert.Equal((QuestionType.MultiSelect, QuestionStatus.Draft), (multi.QuestionType, multi.Status));
        Assert.Equal([true, false, true], multi.Options.OrderBy(o => o.SortOrder).Select(o => o.IsCorrect));

        // The key is used up: confirming twice saves nothing more.
        using var again = await admin.PostFormAsync(Page, new Dictionary<string, string> { ["key"] = key }, $"{Page}?handler=Confirm");
        Assert.Contains("Hasil pemeriksaan sudah kedaluwarsa.", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(500, await CountAsync(topic));
    }

    [Fact]
    public async Task The_template_downloads_as_xlsx()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var response = await admin.GetAsync(new Uri($"{Page}?handler=Template", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal((byte)'P', bytes[0]); // a zip package
    }

    [Fact]
    public async Task File_problems_are_shown_without_a_preview()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var wrongType = await UploadAsync(admin, "hello", "soal.txt");
        using var noColumns = await UploadAsync(admin, "a,b\r\n1,2", "soal.csv");

        Assert.Contains("Gunakan file .xlsx atau .csv.", WebUtility.HtmlDecode(await wrongType.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Contains("Kolom wajib tidak ada: type, text, correct, explanation, topic.", WebUtility.HtmlDecode(await noColumns.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Learners_cannot_import()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri(Page, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Helpers ----------

    private static string Row(string type, string text, string correct, string topic) =>
        string.Join(",", new[] { type, text, "pilihan satu", "pilihan dua", "pilihan tiga", correct, "Pembahasan.", topic }.Select(v => $"\"{v}\""));

    private static string Csv(params string[] rows) =>
        "type,text,option_a,option_b,option_c,correct,explanation,topic\r\n" + string.Join("\r\n", rows);

    private async Task<string> TopicAsync()
    {
        var name = "Impor " + TestData.Unique();
        await using var db = database.CreateContext();
        db.Topics.Add(new Topic { Name = name, Subject = new Subject { Name = "Subjek " + TestData.Unique() } });
        await db.SaveChangesAsync();
        return name;
    }

    private async Task<int> CountAsync(string topic)
    {
        await using var db = database.CreateContext();
        return await db.Questions.CountAsync(q => q.Topic!.Name == topic);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string content, string fileName, string status = "Active")
    {
        var token = await client.GetAntiforgeryTokenAsync(Page);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent(status), "Status" },
        };
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        return await client.PostAsync(new Uri($"{Page}?handler=Preview", UriKind.Relative), form);
    }

    [GeneratedRegex("name=\"key\" value=\"([0-9a-f-]+)\"")]
    private static partial Regex KeyPattern();
}
