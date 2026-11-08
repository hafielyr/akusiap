using Akusiap.Application.Common;
using Akusiap.Application.UpcomingTests;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>CPNS-01: what an upcoming test needs before it is saved, and how its outline reads.</summary>
public sealed class UpcomingTestRulesTests
{
    [Fact]
    public void A_complete_test_has_no_problems() => Assert.Empty(UpcomingTestRules.Validate(Valid()));

    [Theory]
    [InlineData("CPNS-2026")]
    [InlineData("cpns 2026")]
    [InlineData("-cpns")]
    [InlineData("cpns--2026")]
    public void A_slug_must_be_lowercase_words_joined_by_hyphens(string slug) =>
        Assert.Contains(UpcomingTestRules.Validate(Valid() with { Slug = slug }), p => p.Code == UpcomingTestRules.SlugInvalid);

    [Fact]
    public void Name_organizer_category_and_status_are_required()
    {
        var problems = UpcomingTestRules.Validate(Valid() with { Name = " ", Organizer = null, Category = null, ScheduleStatus = null });
        Assert.Equal(4, problems.Count(p => p.Code == TextRules.Required));
    }

    [Fact]
    public void An_official_schedule_needs_a_date() =>
        Assert.Contains(
            UpcomingTestRules.Validate(Valid() with { ScheduleStatus = ScheduleStatus.Official, TestStartsOn = null, RegistrationOpensOn = null }),
            p => p.Code == UpcomingTestRules.OfficialNeedsDate);

    [Fact]
    public void The_test_cannot_come_before_registration() =>
        Assert.Contains(
            UpcomingTestRules.Validate(Valid() with { RegistrationOpensOn = new DateOnly(2027, 2, 1), TestStartsOn = new DateOnly(2027, 1, 1) }),
            p => p.Code == UpcomingTestRules.DatesOrder);

    [Fact]
    public void The_source_must_be_a_web_link() =>
        Assert.Contains(UpcomingTestRules.Validate(Valid() with { SourceUrl = "bkn.go.id" }), p => p.Code == UpcomingTestRules.SourceUrlInvalid);

    [Fact]
    public void The_outline_splits_into_headed_blocks()
    {
        var blocks = UpcomingTest.ParseOutline("Format\n110 soal\n- 100 menit\n\n\nTWK\nNasionalisme\n");

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Format", blocks[0].Heading);
        Assert.Equal(["110 soal", "100 menit"], blocks[0].Items);
        Assert.Equal(["Nasionalisme"], blocks[1].Items);
    }

    [Fact]
    public void Days_left_count_to_the_test_day_else_the_registration_day()
    {
        var today = new DateOnly(2026, 11, 9);
        Assert.Equal(70, new UpcomingTest { TestStartsOn = new DateOnly(2027, 1, 18), RegistrationOpensOn = new DateOnly(2026, 11, 20) }.DaysLeft(today));
        Assert.Equal(11, new UpcomingTest { RegistrationOpensOn = new DateOnly(2026, 11, 20) }.DaysLeft(today));
        Assert.Null(new UpcomingTest().DaysLeft(today));
    }

    private static SaveUpcomingTestRequest Valid() => new(
        null, "cpns-2026", "CPNS 2026", "BKN", UpcomingTestCategory.Cpns, null, null, new DateOnly(2027, 1, 18),
        ScheduleStatus.Estimated, null, null, "https://bkn.go.id", null, true);
}
