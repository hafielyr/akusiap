using Akusiap.Application.Common;
using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T1: what a subject or topic needs before it is saved.</summary>
public sealed class SubjectAndTopicRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Subject_name_is_required(string? name)
    {
        var problem = Assert.Single(SubjectRules.Validate(new SaveSubjectRequest(null, name, null)));

        Assert.Equal(new[] { TextRules.Required, SubjectRules.NameField }, new[] { problem.Code, problem.Field });
        Assert.Equal(SubjectRules.NameLabel, Assert.Single(problem.Args));
    }

    [Fact]
    public void Subject_name_may_be_200_characters_but_not_more()
    {
        Assert.Empty(SubjectRules.Validate(new SaveSubjectRequest(null, new string('a', 200), null)));

        var problem = Assert.Single(SubjectRules.Validate(new SaveSubjectRequest(null, new string('a', 201), null)));
        Assert.Equal(TextRules.TooLong, problem.Code);
        Assert.Equal(new object[] { SubjectRules.NameLabel, 200 }, problem.Args);
    }

    [Fact]
    public void Subject_name_is_measured_after_trimming()
    {
        Assert.Empty(SubjectRules.Validate(new SaveSubjectRequest(null, "  " + new string('a', 200) + "  ", null)));
    }

    [Fact]
    public void Description_is_optional_up_to_1000_characters()
    {
        Assert.Empty(SubjectRules.Validate(new SaveSubjectRequest(null, "CPNS", null)));
        Assert.Empty(SubjectRules.Validate(new SaveSubjectRequest(null, "CPNS", new string('d', 1000))));

        var problem = Assert.Single(SubjectRules.Validate(new SaveSubjectRequest(null, "CPNS", new string('d', 1001))));
        Assert.Equal((TextRules.TooLong, SubjectRules.DescriptionField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Topic_needs_a_name_and_a_subject()
    {
        var problems = TopicRules.Validate(new SaveTopicRequest(null, 0, " ", null));

        Assert.Equal(
            new[] { (TextRules.Required, TopicRules.NameField), (TopicRules.SubjectMissing, TopicRules.SubjectField) },
            problems.Select(p => (p.Code, p.Field!)).ToArray());
    }

    [Fact]
    public void Valid_topic_has_no_problems()
    {
        Assert.Empty(TopicRules.Validate(new SaveTopicRequest(null, 7, "Tes Wawasan Kebangsaan", "TWK")));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("  TWK ", "TWK")]
    public void Clean_trims_and_turns_blank_into_null(string? input, string? expected)
    {
        Assert.Equal(expected, TextRules.Clean(input));
    }
}
