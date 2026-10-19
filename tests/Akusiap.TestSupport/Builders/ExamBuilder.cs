using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.TestSupport.Builders;

/// <summary>Builds a published app exam (free and without questions unless told otherwise).</summary>
public sealed class ExamBuilder
{
    private string _title = "Tryout " + TestData.Unique();
    private ExamStatus _status = ExamStatus.Published;
    private bool _isFree = true;
    private long? _priceIdr;
    private int? _accessDays;
    private int _questionCount;
    private int _trialCount;
    private Guid? _ownerId;
    private OwnerKind _ownerKind = OwnerKind.App;
    private RulePreset? _rulePreset;

    public ExamBuilder Titled(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>A paid app exam with the given Rupiah price and 30 days of access.</summary>
    public ExamBuilder Paid(long priceIdr, int accessDays = 30)
    {
        _isFree = false;
        _priceIdr = priceIdr;
        _accessDays = accessDays;
        return this;
    }

    public ExamBuilder Free()
    {
        _isFree = true;
        _priceIdr = null;
        _accessDays = null;
        return this;
    }

    public ExamBuilder WithStatus(ExamStatus status)
    {
        _status = status;
        return this;
    }

    public ExamBuilder Draft() => WithStatus(ExamStatus.Draft);

    /// <summary>Adds <paramref name="count"/> single-choice questions; the first <paramref name="trial"/> are in the trial set.</summary>
    public ExamBuilder WithQuestions(int count, int trial = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(trial, count);
        _questionCount = count;
        _trialCount = trial;
        return this;
    }

    /// <summary>
    /// A teacher's or learner's own exam (and its questions), linked by id: save <paramref name="owner"/> first. Own exams are
    /// never sold, so this also makes it free.
    /// </summary>
    public ExamBuilder OwnedBy(User owner, OwnerKind kind = OwnerKind.Learner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (kind == OwnerKind.App)
        {
            throw new ArgumentException("App exams have no owner.", nameof(kind));
        }

        _ownerId = owner.Id;
        _ownerKind = kind;
        return Free();
    }

    public ExamBuilder WithRules(RulePreset preset)
    {
        _rulePreset = preset;
        return this;
    }

    public Exam Build()
    {
        var subject = new Subject { Name = "Subjek " + TestData.Unique() };
        var topic = new Topic { Subject = subject, Name = "Topik " + TestData.Unique() };
        var exam = new Exam
        {
            OwnerUserId = _ownerId,
            OwnerKind = _ownerKind,
            Subject = subject,
            RulePreset = _rulePreset ?? new RulePreset { Name = "Aturan " + TestData.Unique() },
            Title = _title,
            IsFree = _isFree,
            PriceIdr = _priceIdr,
            AccessDays = _accessDays,
            Status = _status,
            CreatedAt = TestData.Now,
        };

        for (var i = 1; i <= _questionCount; i++)
        {
            exam.Questions.Add(new ExamQuestion
            {
                Question = NewQuestion(topic, _ownerId, i),
                SortOrder = i,
                IsTrial = i <= _trialCount,
            });
        }

        return exam;
    }

    public static implicit operator Exam(ExamBuilder builder) => (builder ?? throw new ArgumentNullException(nameof(builder))).Build();

    private static Question NewQuestion(Topic topic, Guid? ownerId, int number)
    {
        var question = new Question
        {
            OwnerUserId = ownerId,
            Topic = topic,
            QuestionType = QuestionType.SingleChoice,
            Text = $"Soal nomor {number}?",
            Explanation = $"Pembahasan soal nomor {number}.",
            KeyFact = $"Fakta kunci {number}",
            Difficulty = Question.MinDifficulty,
            Status = QuestionStatus.Active,
            CreatedAt = TestData.Now,
            UpdatedAt = TestData.Now,
        };
        for (var option = 1; option <= 4; option++)
        {
            question.Options.Add(new QuestionOption { Text = $"Pilihan {option}", IsCorrect = option == 1, SortOrder = option });
        }

        return question;
    }
}
