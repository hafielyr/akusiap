namespace Akusiap.Domain.Content;

/// <summary>What kind of hiring test an <see cref="UpcomingTest"/> is (CPNS-01). Used for the filter chips.</summary>
public enum UpcomingTestCategory
{
    Cpns,
    Pppk,
    Kedinasan,
    Bumn,
    TniPolri,
    Banking,
    Other,
}

/// <summary>How sure the dates of an <see cref="UpcomingTest"/> are (CPNS-01, D-129).</summary>
public enum ScheduleStatus
{
    /// <summary>The organiser published the dates.</summary>
    Official,

    /// <summary>Worked out from earlier cycles or news; the page says so.</summary>
    Estimated,

    /// <summary>No date yet; the test is listed so people can prepare.</summary>
    NotAnnounced,
}

/// <summary>
/// A hiring test that is coming up (CPNS-01, D-129): CPNS, PPPK, sekolah kedinasan, BUMN and so on. Admins keep the list;
/// the public page <c>/jadwal-tes</c> shows it by date and links the test to the exam package that prepares for it.
/// Not in schema.sql.
/// </summary>
public class UpcomingTest
{
    public const int SlugMaxLength = 80;
    public const int NameMaxLength = 150;
    public const int OrganizerMaxLength = 150;
    public const int SummaryMaxLength = 300;
    public const int ScheduleNoteMaxLength = 300;
    public const int OutlineMaxLength = 8000;
    public const int SourceUrlMaxLength = 1000;

    public long Id { get; set; }

    /// <summary>Lowercase letters, digits and hyphens; the page address <c>/jadwal-tes/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Who runs the test, for example "BKN dan KemenPAN-RB".</summary>
    public string Organizer { get; set; } = string.Empty;

    public UpcomingTestCategory Category { get; set; }

    /// <summary>One line for the list.</summary>
    public string? Summary { get; set; }

    /// <summary>Jakarta date registration opens, when known.</summary>
    public DateOnly? RegistrationOpensOn { get; set; }

    /// <summary>Jakarta date the (first) test day starts, when known.</summary>
    public DateOnly? TestStartsOn { get; set; }

    public ScheduleStatus ScheduleStatus { get; set; } = ScheduleStatus.NotAnnounced;

    /// <summary>Plain words about the dates, for example "Pendaftaran direncanakan November 2026".</summary>
    public string? ScheduleNote { get; set; }

    /// <summary>
    /// What the test covers, as plain text: blocks split by a blank line, the first line of a block is its heading and
    /// each following line one item.
    /// </summary>
    public string? Outline { get; set; }

    /// <summary>Where the dates come from (an absolute http or https link).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>The app exam sold as this test's package, if any.</summary>
    public long? ExamId { get; set; }

    public Exam? Exam { get; set; }

    public bool IsPublished { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>The date the list sorts by: the test day, else the registration day.</summary>
    public DateOnly? KeyDate => TestStartsOn ?? RegistrationOpensOn;

    /// <summary>Days from <paramref name="today"/> to <see cref="KeyDate"/>; null without a date.</summary>
    public int? DaysLeft(DateOnly today) => KeyDate is { } date ? date.DayNumber - today.DayNumber : null;

    /// <summary>Whether <paramref name="slug"/> is 1–80 characters of a–z, 0–9 and single inner hyphens.</summary>
    public static bool IsValidSlug(string? slug)
    {
        if (string.IsNullOrEmpty(slug) || slug.Length > SlugMaxLength || slug[0] == '-' || slug[^1] == '-' || slug.Contains("--", StringComparison.Ordinal))
        {
            return false;
        }

        return slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }

    /// <summary>Splits <see cref="Outline"/> into headed blocks; empty when there is no outline.</summary>
    public static IReadOnlyList<OutlineBlock> ParseOutline(string? outline)
    {
        if (string.IsNullOrWhiteSpace(outline))
        {
            return [];
        }

        var blocks = new List<OutlineBlock>();
        var lines = outline.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? heading = null;
        var items = new List<string>();
        foreach (var raw in lines.Append(string.Empty))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (heading is not null)
                {
                    blocks.Add(new OutlineBlock(heading, [.. items]));
                }

                heading = null;
                items.Clear();
                continue;
            }

            if (heading is null)
            {
                heading = line;
            }
            else
            {
                items.Add(line.TrimStart('-', '*', '•').Trim());
            }
        }

        return blocks;
    }
}

/// <summary>One block of an <see cref="UpcomingTest.Outline"/>: a heading and its items.</summary>
public sealed record OutlineBlock(string Heading, IReadOnlyList<string> Items);
