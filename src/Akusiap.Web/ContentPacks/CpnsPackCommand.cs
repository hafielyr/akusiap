using System.Globalization;
using Akusiap.Application.ContentPacks;
using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Web.ContentPacks;

/// <summary>
/// The <c>load-cpns-pack &lt;folder&gt;</c> command (CPNS-06): applies migrations, loads every import file in the folder
/// (<c>*.xlsx</c> and <c>*.csv</c>, except <c>cpns-semua.xlsx</c>, which is for browsing) in name order with
/// <see cref="CpnsPack"/>, prints the report and exits. It uses the app's own connection string.
/// </summary>
public static class CpnsPackCommand
{
    public const string Name = "load-cpns-pack";

    /// <summary>The bank-soal browsing workbook repeats every other file; it is never imported.</summary>
    public const string BrowseOnlyFile = "cpns-semua.xlsx";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 1 || !Directory.Exists(args[0]))
        {
            await Console.Error.WriteLineAsync($"Usage: {Name} <folder with the question files>").ConfigureAwait(false);
            return 2;
        }

        var paths = Directory.EnumerateFiles(args[0])
            .Where(p => p.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            .Where(p => !string.Equals(Path.GetFileName(p), BrowseOnlyFile, StringComparison.OrdinalIgnoreCase))
            .Where(p => !Path.GetFileName(p).StartsWith('~'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count == 0)
        {
            await Console.Error.WriteLineAsync($"No .xlsx or .csv files in {args[0]}").ConfigureAwait(false);
            return 2;
        }

        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync().ConfigureAwait(false);

        var streams = paths.Select(p => new PackFile(Path.GetFileName(p), File.OpenRead(p))).ToList();
        try
        {
            var report = await scope.ServiceProvider.GetRequiredService<CpnsPack>().LoadAsync(streams, CancellationToken.None).ConfigureAwait(false);
            var culture = CultureInfo.InvariantCulture;
            foreach (var file in report.Files)
            {
                Console.WriteLine(string.Create(culture, $"{file.Name}: {file.Rows} rows, {file.Added} added, {file.Duplicates} already in the bank, {file.Invalid} refused"));
                foreach (var problem in file.Problems)
                {
                    Console.WriteLine($"    row {problem}");
                }
            }

            Console.WriteLine(string.Create(culture, $"Exam {report.ExamId} \"{CpnsPack.ExamTitle}\": {report.PackageQuestions} questions, trial {report.TrialQuestions}"));
            foreach (var (topic, count) in report.PackageByTopic)
            {
                Console.WriteLine(string.Create(culture, $"    {topic}: {count}"));
            }

            Console.WriteLine(string.Create(culture, $"TKP questions with points: {report.WithPoints}. Left as Draft (low quality score): {report.LeftAsDraft}. Upcoming tests added: {report.UpcomingTestsAdded}."));
            Console.WriteLine(string.Create(culture, $"Tryout sets (30 TWK, 35 TIU, 45 TKP, no shared questions): {report.TryoutSets} of {Akusiap.Domain.Sessions.TryoutSets.MaxSets}."));
            return 0;
        }
        finally
        {
            foreach (var file in streams)
            {
                await file.Content.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
