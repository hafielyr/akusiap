using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Questions;
using Akusiap.Application.Questions.Import;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.ContentPacks;

/// <summary>A question file to load: its name (for the report and the xlsx/csv check) and its bytes.</summary>
public sealed record PackFile(string Name, Stream Content);

/// <summary>What happened to one file.</summary>
/// <param name="Invalid">Rows the import rules refused, with the first problem codes.</param>
public sealed record PackFileReport(string Name, int Rows, int Added, int Duplicates, int Invalid, IReadOnlyList<string> Problems);

/// <summary>What the load did: per file, the bank and the package.</summary>
public sealed record PackReport(
    IReadOnlyList<PackFileReport> Files,
    long ExamId,
    int PackageQuestions,
    IReadOnlyDictionary<string, int> PackageByTopic,
    int TrialQuestions,
    int LeftAsDraft,
    int WithPoints,
    int UpcomingTestsAdded);

/// <summary>
/// Loads the CPNS 2026 package (CPNS-06, D-133). It imports every question file (the bank-soal folder), skipping rows
/// that are already in the bank (same text, <see cref="CpnsPackRules.Key"/>) or break the import rules; reads TKP points
/// from the "Skor per pilihan" line when the file has no option_points column; makes questions that score at least
/// <see cref="CpnsPackRules.MinScore"/> Active and the rest Draft; then builds or updates the paid exam "Paket CPNS 2026:
/// SKD" (Rp50.000, the owner's price) with every Active question of the three topics, a 30-question trial (10 per
/// subtest, the app's own questions first) and the SKD tryout preset. Finally it adds the starting list of upcoming tests.
/// Running it again adds only what is new, so it is safe to repeat.
/// </summary>
public sealed class CpnsPack(IAppDbContext db, IQuestionFileReader reader, IClock clock)
{
    public const string ExamTitle = "Paket CPNS 2026: SKD";
    public const string PresetName = "Tryout SKD CPNS";
    public const long PriceIdr = 50_000;
    public const int AccessDays = 180;
    public const int TrialPerTopic = 10;
    private const int MaxRowsPerFile = 20_000;

    public const string ExamDescription =
        "Persiapan SKD CPNS 2026 dalam satu paket: bank soal TWK, TIU dan TKP dengan pembahasan, tryout 110 soal dalam 100 menit dengan tampilan CAT dan nilai per subtes, flashcard dari soal yang salah, serta rencana belajar harian sampai hari tes.\n\n"
        + "Nilai tryout memakai aturan SKD terakhir yang terbit (KepmenPAN-RB No. 321 Tahun 2024): TWK 65, TIU 80, TKP 166. Aturan CPNS 2026 belum terbit; angkanya akan disesuaikan bila berubah.";

    public async Task<PackReport> LoadAsync(IReadOnlyList<PackFile> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(files);
        var now = clock.UtcNow;
        var topics = await EnsureTopicsAsync(cancellationToken).ConfigureAwait(false);
        var directory = new ImportTopicDirectory(await db.Topics
            .Select(t => new ImportTopic(t.Id, t.Name, t.Subject!.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
        var topicIds = topics.Values.ToHashSet();
        var tkpId = topics[CpnsPackRules.TkpTopic];

        var existingTexts = await db.Questions
            .Where(q => topicIds.Contains(q.TopicId) && q.OwnerUserId == null)
            .Select(q => q.Text)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var seen = existingTexts.Select(CpnsPackRules.Key).ToHashSet(StringComparer.Ordinal);

        var reports = new List<PackFileReport>();
        var draft = 0;
        foreach (var file in files)
        {
            var content = reader.Read(file.Content, file.Name, MaxRowsPerFile);
            if (content.FileProblem is { } fileProblem)
            {
                reports.Add(new PackFileReport(file.Name, 0, 0, 0, 0, [fileProblem.Code]));
                continue;
            }

            int added = 0, duplicates = 0, invalid = 0;
            var problems = new List<string>();
            foreach (var row in content.Rows)
            {
                var (request, rowProblems) = QuestionImportMapper.Map(row, directory, QuestionStatus.Active);
                if (rowProblems.Count > 0)
                {
                    invalid++;
                    if (problems.Count < 5)
                    {
                        problems.Add($"{row.RowNumber}: {string.Join(", ", rowProblems.Select(p => p.Code))}");
                    }

                    continue;
                }

                if (!topicIds.Contains(request.TopicId) || !seen.Add(CpnsPackRules.Key(request.Text)))
                {
                    duplicates++;
                    continue;
                }

                var isTkp = request.TopicId == tkpId;
                if (isTkp && request.Options.All(o => o.Points is null) && CpnsPackRules.PointsFromExplanation(request.Explanation, request.Options) is { } points)
                {
                    request = request with { Options = [.. request.Options.Select((o, i) => o with { Points = points[i] })] };
                }

                var active = CpnsPackRules.Score(request, isTkp) >= CpnsPackRules.MinScore;
                draft += active ? 0 : 1;
                db.Questions.Add(ToQuestion(request, active ? QuestionStatus.Active : QuestionStatus.Draft, now));
                added++;
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            reports.Add(new PackFileReport(file.Name, content.Rows.Count, added, duplicates, invalid, problems));
        }

        var exam = await EnsureExamAsync(topics, now, cancellationToken).ConfigureAwait(false);
        var (byTopic, trial) = await FillExamAsync(exam, topics, cancellationToken).ConfigureAwait(false);
        var withPoints = await db.Questions.CountAsync(q => q.TopicId == tkpId && q.Status == QuestionStatus.Active && q.Options.Any(o => o.Points != null), cancellationToken).ConfigureAwait(false);
        var testsAdded = await EnsureUpcomingTestsAsync(exam.Id, now, cancellationToken).ConfigureAwait(false);
        return new PackReport(reports, exam.Id, byTopic.Values.Sum(), byTopic, trial, draft, withPoints, testsAdded);
    }

    private static Question ToQuestion(SaveQuestionRequest request, QuestionStatus status, DateTime now)
    {
        var question = new Question
        {
            TopicId = request.TopicId,
            QuestionType = request.Type!.Value,
            Text = TextRules.Clean(request.Text)!,
            Explanation = TextRules.Clean(request.Explanation)!,
            KeyFact = TextRules.Clean(request.KeyFact),
            Hint = TextRules.Clean(request.Hint),
            Difficulty = request.Difficulty,
            SourceNote = TextRules.Clean(request.SourceNote),
            Origin = request.Origin,
            SourceUrl = TextRules.Clean(request.SourceUrl),
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
        };
        for (var i = 0; i < request.Options.Count; i++)
        {
            question.Options.Add(new QuestionOption
            {
                Text = TextRules.Clean(request.Options[i].Text)!,
                IsCorrect = request.Options[i].IsCorrect,
                SortOrder = i + 1,
                Points = request.Options[i].Points,
            });
        }

        return question;
    }

    /// <summary>The subject "Seleksi CPNS" and its three topics, created when missing. Returns topic name → id.</summary>
    private async Task<Dictionary<string, long>> EnsureTopicsAsync(CancellationToken cancellationToken)
    {
        var subject = await db.Subjects.SingleOrDefaultAsync(s => s.Name == CpnsPackRules.SubjectName, cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            subject = new Subject { Name = CpnsPackRules.SubjectName, Description = "Seleksi Kompetensi Dasar (SKD) CPNS: TWK, TIU dan TKP." };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var existing = await db.Topics.Where(t => t.SubjectId == subject.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var name in CpnsPackRules.Topics.Where(n => existing.All(t => t.Name != n)))
        {
            var topic = new Topic { SubjectId = subject.Id, Name = name };
            db.Topics.Add(topic);
            existing.Add(topic);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CpnsPackRules.Topics.ToDictionary(n => n, n => existing.Single(t => t.Name == n).Id);
    }

    /// <summary>The package exam and its tryout preset, created when missing; price, scheme and access are kept current.</summary>
    private async Task<Exam> EnsureExamAsync(Dictionary<string, long> topics, DateTime now, CancellationToken cancellationToken)
    {
        var preset = await db.RulePresets.SingleOrDefaultAsync(p => p.OwnerUserId == null && p.Name == PresetName, cancellationToken).ConfigureAwait(false);
        if (preset is null)
        {
            preset = new RulePreset
            {
                Name = PresetName,
                TimeLimitMinutes = Domain.Sessions.SkdScoring.TimeLimitMinutes,
                QuestionCount = Domain.Sessions.SkdScoring.QuestionCount,
                HintsEnabled = true,
                ShuffleQuestions = true,
                ShuffleOptions = true,
                ShowAnswers = ShowAnswers.AfterSubmit,
                CatMode = true,
            };
            db.RulePresets.Add(preset);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var subjectId = await db.Topics.Where(t => t.Id == topics[CpnsPackRules.TwkTopic]).Select(t => t.SubjectId).SingleAsync(cancellationToken).ConfigureAwait(false);
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.OwnerKind == OwnerKind.App && e.Title == ExamTitle, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            exam = new Exam { OwnerKind = OwnerKind.App, Title = ExamTitle, CreatedAt = now };
            db.Exams.Add(exam);
        }

        exam.SubjectId = subjectId;
        exam.RulePresetId = preset.Id;
        exam.Description = ExamDescription;
        exam.IsFree = false;
        exam.PriceIdr = PriceIdr;
        exam.AccessDays = AccessDays;
        exam.ScoringScheme = ScoringScheme.Skd;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return exam;
    }

    /// <summary>
    /// Puts every Active question of the three topics in the exam (new ones at the end), takes Draft ones out, picks the
    /// trial set when it is incomplete, and publishes the exam. Returns the count per topic and the trial size.
    /// </summary>
    private async Task<(Dictionary<string, int> ByTopic, int Trial)> FillExamAsync(Exam exam, Dictionary<string, long> topics, CancellationToken cancellationToken)
    {
        var topicIds = topics.Values.ToList();
        var active = await db.Questions
            .Where(q => topicIds.Contains(q.TopicId) && q.OwnerUserId == null && q.Status == QuestionStatus.Active)
            .Select(q => new { q.Id, q.TopicId, q.Origin, Explanation = q.Explanation.Length, q.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var activeIds = active.Select(q => q.Id).ToHashSet();
        var links = await db.ExamQuestions.Where(x => x.ExamId == exam.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var gone in links.Where(l => !activeIds.Contains(l.QuestionId)).ToList())
        {
            db.ExamQuestions.Remove(gone);
            links.Remove(gone);
        }

        var linked = links.Select(l => l.QuestionId).ToHashSet();
        var order = links.Count == 0 ? 0 : links.Max(l => l.SortOrder);
        var topicOrder = topicIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        foreach (var question in active.Where(q => !linked.Contains(q.Id)).OrderBy(q => topicOrder[q.TopicId]).ThenBy(q => q.Id))
        {
            var link = new ExamQuestion { ExamId = exam.Id, QuestionId = question.Id, SortOrder = ++order };
            db.ExamQuestions.Add(link);
            links.Add(link);
        }

        if (links.Count(l => l.IsTrial) < TrialSetRule.MinTrial)
        {
            foreach (var link in links)
            {
                link.IsTrial = false;
            }

            var byId = active.ToDictionary(q => q.Id);
            foreach (var topicId in topicIds)
            {
                var picks = links
                    .Select(l => byId[l.QuestionId])
                    .Where(q => q.TopicId == topicId)
                    .OrderByDescending(q => q.Origin == QuestionOrigin.Original)
                    .ThenByDescending(q => q.Explanation)
                    .Take(TrialPerTopic)
                    .Select(q => q.Id)
                    .ToHashSet();
                foreach (var link in links.Where(l => picks.Contains(l.QuestionId)))
                {
                    link.IsTrial = true;
                }
            }
        }

        var trial = links.Count(l => l.IsTrial);
        if (TrialSetRule.For(links.Count).IsMetBy(trial) && links.Count > 0)
        {
            exam.Status = ExamStatus.Published;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var inExam = links.Select(l => l.QuestionId).ToHashSet();
        var counts = active.Where(q => inExam.Contains(q.Id)).GroupBy(q => q.TopicId).ToDictionary(g => g.Key, g => g.Count());
        return (topics.ToDictionary(t => t.Key, t => counts.GetValueOrDefault(t.Value)), trial);
    }

    /// <summary>Adds the starting list of upcoming tests whose slug is not taken yet; links CPNS 2026 to the package.</summary>
    private async Task<int> EnsureUpcomingTestsAsync(long examId, DateTime now, CancellationToken cancellationToken)
    {
        var slugs = await db.UpcomingTests.Select(t => t.Slug).ToListAsync(cancellationToken).ConfigureAwait(false);
        var added = 0;
        foreach (var test in UpcomingTestSeed.All().Where(t => !slugs.Contains(t.Slug)))
        {
            test.CreatedAt = now;
            test.UpdatedAt = now;
            test.IsPublished = true;
            if (test.Slug == UpcomingTestSeed.CpnsSlug)
            {
                test.ExamId = examId;
            }

            db.UpcomingTests.Add(test);
            added++;
        }

        var cpns = await db.UpcomingTests.SingleOrDefaultAsync(t => t.Slug == UpcomingTestSeed.CpnsSlug, cancellationToken).ConfigureAwait(false);
        if (cpns is { ExamId: null })
        {
            cpns.ExamId = examId;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return added;
    }
}
