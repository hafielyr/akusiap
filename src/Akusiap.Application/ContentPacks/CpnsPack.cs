using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Questions;
using Akusiap.Application.Questions.Import;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
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
    int UpcomingTestsAdded)
{
    /// <summary>Complete tryout sets of 30 TWK, 35 TIU and 45 TKP (CPNS-07).</summary>
    public int TryoutSets { get; init; }
}

/// <summary>
/// Loads the CPNS 2026 package (CPNS-06, D-133). It imports every question file (the bank-soal folder), skipping rows
/// that are already in the bank (same text, <see cref="CpnsPackRules.Key"/>) or break the import rules; reads TKP points
/// from the "Skor per pilihan" line when the file has no option_points column; makes questions that score at least
/// <see cref="CpnsPackRules.MinScore"/> Active and the rest Draft; then builds or updates the paid exam "Paket CPNS 2026:
/// SKD" (Rp39.000, normally Rp80.000, 3 months: the owner's update of 2026-11-14) with up to 10 fixed tryout sets of 30
/// TWK, 35 TIU and 45 TKP, no question in two sets (CPNS-07, D-142), a 30-question trial (10 per subtest, the app's own
/// questions first) and the SKD tryout preset. Finally it adds the starting list of upcoming tests. Running it again adds
/// only what is new and keeps the sets learners already took, so it is safe to repeat.
/// </summary>
public sealed class CpnsPack(IAppDbContext db, IQuestionFileReader reader, IClock clock)
{
    public const string ExamTitle = "Paket CPNS 2026: SKD";
    public const string PresetName = "Tryout SKD CPNS";
    public const long PriceIdr = 39_000;
    public const long OriginalPriceIdr = 80_000;
    public const int AccessDays = 90;
    public const int TrialPerTopic = 10;
    private const int MaxRowsPerFile = 20_000;

    /// <summary>
    /// The package description (owner text, 2026-11-14) with the real counts: the owner's numbers (1.100 questions, TWK 300,
    /// TIU 350, TKP 450, 10 tryouts) once the bank fills all ten sets.
    /// </summary>
    public static string Description(int twk, int tiu, int tkp, int tryouts)
    {
        var id = System.Globalization.CultureInfo.GetCultureInfo("id-ID");
        var bank = string.Create(id, $"{twk + tiu + tkp:N0} soal dengan pembahasan dan kunci jawaban (TWK {twk:N0}, TIU {tiu:N0}, TKP {tkp:N0})");
        var tryout = tryouts > 0
            ? string.Create(id, $"{tryouts} paket tryout, masing-masing 110 soal (30 TWK, 35 TIU, 45 TKP) dalam 100 menit, dengan nilai per subtes")
            : "Tryout 110 soal, 100 menit, dengan nilai per subtes";
        return string.Join(
            '\n',
            bank,
            tryout,
            "Bisa dikerjakan berulang kali",
            "Analisis tiap sesi: topik terlemah, kecepatan mengerjakan soal, dan ambang batas",
            "Flashcard dari soal yang salah",
            "Rencana belajar harian sampai hari tes");
    }

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
        var (byTopic, trial, sets) = await FillExamAsync(exam, topics, cancellationToken).ConfigureAwait(false);
        var withPoints = await db.Questions.CountAsync(q => q.TopicId == tkpId && q.Status == QuestionStatus.Active && q.Options.Any(o => o.Points != null), cancellationToken).ConfigureAwait(false);
        var testsAdded = await EnsureUpcomingTestsAsync(exam.Id, now, cancellationToken).ConfigureAwait(false);
        return new PackReport(reports, exam.Id, byTopic.Values.Sum(), byTopic, trial, draft, withPoints, testsAdded) { TryoutSets = sets };
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
                // Latihan checks each answer and shows its explanation (FIX-04, D-143); the tryout always shows them at the end.
                ShowAnswers = ShowAnswers.AfterEach,
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
        exam.IsFree = false;
        exam.PriceIdr = PriceIdr;
        exam.OriginalPriceIdr = OriginalPriceIdr;
        exam.AccessDays = AccessDays;
        exam.ScoringScheme = ScoringScheme.Skd;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return exam;
    }

    /// <summary>
    /// Fills the exam (CPNS-07, D-142): up to 10 fixed tryout sets of 30 TWK, 35 TIU and 45 TKP from the Active questions
    /// of the three topics, best first (TKP with points first, then the app's own questions, then longer explanations),
    /// keeping the sets learners already took. The exam holds exactly the questions of the complete sets; with not even
    /// one complete set it holds every Active question, as before. Then it picks the trial set when it is incomplete,
    /// writes the description with the counts and publishes the exam. Returns the count per topic, the trial size and the
    /// number of sets.
    /// </summary>
    private async Task<(Dictionary<string, int> ByTopic, int Trial, int Sets)> FillExamAsync(Exam exam, Dictionary<string, long> topics, CancellationToken cancellationToken)
    {
        var topicIds = topics.Values.ToList();
        var subtestOf = new Dictionary<long, SkdSubtest>
        {
            [topics[CpnsPackRules.TwkTopic]] = SkdSubtest.Twk,
            [topics[CpnsPackRules.TiuTopic]] = SkdSubtest.Tiu,
            [topics[CpnsPackRules.TkpTopic]] = SkdSubtest.Tkp,
        };
        var active = await db.Questions
            .Where(q => topicIds.Contains(q.TopicId) && q.OwnerUserId == null && q.Status == QuestionStatus.Active)
            .Select(q => new { q.Id, q.TopicId, q.Origin, Explanation = q.Explanation.Length, HasPoints = q.Options.Any(o => o.Points != null) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var links = await db.ExamQuestions.Where(x => x.ExamId == exam.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var currentSet = links.Where(l => l.TryoutSet != null).ToDictionary(l => l.QuestionId, l => (int?)l.TryoutSet);
        var ranked = active
            .OrderByDescending(q => q.HasPoints)
            .ThenByDescending(q => q.Origin == QuestionOrigin.Original)
            .ThenByDescending(q => q.Explanation)
            .ThenBy(q => q.Id)
            .ToList();
        var sets = TryoutSets.Assign([.. ranked.Select(q => new TryoutCandidate(q.Id, subtestOf[q.TopicId], currentSet.GetValueOrDefault(q.Id)))]);
        var setCount = sets.Values.Distinct().Count();
        var rank = ranked.Select((q, i) => (q.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var content = (setCount > 0 ? ranked.Where(q => sets.ContainsKey(q.Id)) : ranked)
            .OrderBy(q => sets.TryGetValue(q.Id, out var set) ? set : 0)
            .ThenBy(q => subtestOf[q.TopicId])
            .ThenBy(q => rank[q.Id])
            .ToList();

        var keep = content.Select(q => q.Id).ToHashSet();
        foreach (var gone in links.Where(l => !keep.Contains(l.QuestionId)).ToList())
        {
            db.ExamQuestions.Remove(gone);
            links.Remove(gone);
        }

        var byQuestion = links.ToDictionary(l => l.QuestionId);
        var order = 0;
        foreach (var question in content)
        {
            if (!byQuestion.TryGetValue(question.Id, out var link))
            {
                link = new ExamQuestion { ExamId = exam.Id, QuestionId = question.Id };
                db.ExamQuestions.Add(link);
                links.Add(link);
            }

            link.SortOrder = ++order;
            link.TryoutSet = sets.TryGetValue(question.Id, out var set) ? (short)set : null;
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

        var counts = content.GroupBy(q => q.TopicId).ToDictionary(g => g.Key, g => g.Count());
        exam.Description = Description(
            counts.GetValueOrDefault(topics[CpnsPackRules.TwkTopic]),
            counts.GetValueOrDefault(topics[CpnsPackRules.TiuTopic]),
            counts.GetValueOrDefault(topics[CpnsPackRules.TkpTopic]),
            setCount);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (topics.ToDictionary(t => t.Key, t => counts.GetValueOrDefault(t.Value)), trial, setCount);
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
