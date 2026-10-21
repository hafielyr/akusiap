using Akusiap.Application.Abstractions;
using Akusiap.Infrastructure.Email;

namespace Akusiap.IntegrationTests.Infrastructure;

public sealed class FileEmailSenderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "akusiap-mail-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Writes_one_eml_file_with_recipient_subject_and_body()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EmailOptions { From = "LatihanSoal <no-reply@latihansoal.local>" });
        var sender = new FileEmailSender(options, _directory);

        await sender.SendAsync(
            new EmailMessage("budi@example.com", "Konfirmasi email Anda", "<p>Halo Budi</p>", "Halo Budi"),
            CancellationToken.None);

        var file = Assert.Single(Directory.GetFiles(_directory, "*.eml"));
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("To: budi@example.com", content, StringComparison.Ordinal);
        Assert.Contains("<no-reply@latihansoal.local>", content, StringComparison.Ordinal);
        Assert.Contains("Subject: Konfirmasi email Anda", content, StringComparison.Ordinal);
        Assert.Contains("text/html", content, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
