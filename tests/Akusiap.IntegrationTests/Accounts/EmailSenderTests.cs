using Akusiap.Application.Abstractions;
using Akusiap.Infrastructure;
using Akusiap.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>AC-02: which email sender each environment gets, and the dev sender's <c>.eml</c> files.</summary>
public sealed class EmailSenderTests
{
    [Fact]
    public async Task Development_sender_writes_a_utf8_eml_file_with_html_and_text()
    {
        var directory = Path.Combine(Path.GetTempPath(), "akusiap-mail-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sender = new FileEmailSender(Options.Create(new EmailOptions()), directory);

            await sender.SendAsync(new EmailMessage("rina@example.com", "Konfirmasi email", "<p>Halo Rina</p>", "Halo Rina"), CancellationToken.None);

            var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("To: rina@example.com", content, StringComparison.Ordinal);
            Assert.Contains("text/html", content, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Development", null, typeof(FileEmailSender))]
    [InlineData("Production", "smtp-relay.example.com", typeof(SmtpEmailSender))]
    [InlineData("Production", null, typeof(UnconfiguredEmailSender))]
    public void Each_environment_gets_its_sender(string environmentName, string? smtpHost, Type expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Database=unused",
                ["Email:Smtp:Host"] = smtpHost,
            })
            .Build();
        var environment = new HostingEnvironment { EnvironmentName = environmentName, ContentRootPath = Path.GetTempPath() };
        var services = new ServiceCollection().AddLogging();

        services.AddInfrastructure(configuration, environment);
        using var provider = services.BuildServiceProvider();

        Assert.IsType(expected, provider.GetRequiredService<IEmailSender>());
    }
}
