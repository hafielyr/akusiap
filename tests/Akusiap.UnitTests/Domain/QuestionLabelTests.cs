using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>SES-12 (D-138): the section and topic shown on each question.</summary>
public sealed class QuestionLabelTests
{
    [Theory]
    [InlineData("Tes Wawasan Kebangsaan (TWK)", "TWK", "Tes Wawasan Kebangsaan")]
    [InlineData("TIU · Numerik", "TIU", "Numerik")]
    [InlineData("tkp - Pelayanan Publik", "TKP", "Pelayanan Publik")]
    [InlineData("TWK", "TWK", null)]
    [InlineData("Matematika", null, "Matematika")]
    [InlineData("TWK dan TIU", null, "TWK dan TIU")]
    [InlineData("  Farmakologi (lanjutan) ", null, "Farmakologi (lanjutan)")]
    public void A_topic_splits_into_section_and_topic(string name, string? section, string? topic) =>
        Assert.Equal(new QuestionLabel(section, topic), QuestionLabel.From(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_topic_means_no_label(string? name) => Assert.Equal(QuestionLabel.None, QuestionLabel.From(name));
}
