using System.Globalization;
using Akusiap.Domain.Content;
using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Infrastructure.Seeding;

/// <summary>Development-only sample content: one subject with topics, a paid exam (40 questions, 30 trial) and a free exam.</summary>
internal static class SampleContent
{
    public const string SubjectName = "Contoh: Seleksi CPNS";
    public const string PaidExamTitle = "Contoh Tryout SKD CPNS";
    public const string FreeExamTitle = "Contoh Latihan Gratis TIU";
    public const long PaidExamPriceIdr = 49_000;
    public const int PaidExamQuestions = 40;
    public const int PaidExamTrialQuestions = 30;
    public const int FreeExamQuestions = 10;

    private static readonly string[] TopicNames =
    [
        "Tes Wawasan Kebangsaan (TWK)",
        "Tes Intelegensia Umum (TIU)",
        "Tes Karakteristik Pribadi (TKP)",
    ];

    public static async Task SeedAsync(AppDbContext db, RulePreset preset, DateTime now, CancellationToken cancellationToken)
    {
        if (await db.Subjects.AnyAsync(s => s.Name == SubjectName, cancellationToken))
        {
            return;
        }

        var subject = new Subject { Name = SubjectName, Description = "Data contoh untuk pengembangan. Bukan soal resmi." };
        var topics = TopicNames.Select(name => new Topic { Subject = subject, Name = name }).ToArray();

        var paid = NewExam(subject, preset, PaidExamTitle, now);
        paid.IsFree = false;
        paid.PriceIdr = PaidExamPriceIdr;
        paid.AccessDays = 30;
        for (var i = 1; i <= PaidExamQuestions; i++)
        {
            var topic = topics[(i - 1) % topics.Length];
            paid.Questions.Add(new ExamQuestion
            {
                Question = NewQuestion(topic, i, now),
                SortOrder = i,
                IsTrial = i <= PaidExamTrialQuestions,
            });
        }

        var free = NewExam(subject, preset, FreeExamTitle, now);
        free.IsFree = true;
        for (var i = 1; i <= FreeExamQuestions; i++)
        {
            free.Questions.Add(new ExamQuestion { Question = NewQuestion(topics[1], 100 + i, now), SortOrder = i });
        }

        db.AddRange(paid, free);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Exam NewExam(Subject subject, RulePreset preset, string title, DateTime now) => new()
    {
        OwnerKind = OwnerKind.App,
        Subject = subject,
        RulePreset = preset,
        Title = title,
        Description = "Ujian contoh untuk pengembangan.",
        Status = ExamStatus.Published,
        CreatedAt = now,
    };

    private static Question NewQuestion(Topic topic, int number, DateTime now)
    {
        var a = 3 + (number % 7);
        var b = 2 + (number % 5);
        var answer = a * b;
        var question = new Question
        {
            Topic = topic,
            QuestionType = QuestionType.SingleChoice,
            Text = string.Create(CultureInfo.InvariantCulture, $"Contoh soal {number}: berapakah {a} × {b}?"),
            Explanation = string.Create(CultureInfo.InvariantCulture, $"{a} × {b} = {answer}."),
            KeyFact = string.Create(CultureInfo.InvariantCulture, $"{a} × {b} = {answer}"),
            Hint = "Kalikan kedua bilangan.",
            Difficulty = (short)(1 + (number % 5)),
            SourceNote = "Data contoh",
            Status = QuestionStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
        };

        int[] offsets = [0, 1, -1, 2];
        for (var i = 0; i < offsets.Length; i++)
        {
            question.Options.Add(new QuestionOption
            {
                Text = (answer + offsets[(i + number) % offsets.Length]).ToString(CultureInfo.InvariantCulture),
                IsCorrect = offsets[(i + number) % offsets.Length] == 0,
                SortOrder = i + 1,
            });
        }

        return question;
    }
}
