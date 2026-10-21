using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Classes;
using Akusiap.Domain.Content;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Gamification;

/// <summary>GAM-01 (T-09 to T-14): a teacher's class from creation to results, and removing a student.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ClassTests(PostgresFixture database)
{
    [Fact]
    public async Task Teacher_creates_assigns_and_sees_results_and_the_class_leaderboard()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var teacherUser = TestData.User().Named("Bu Guru").Build();
        var studentUser = TestData.User().Named("Murid Satu").Build();
        using var teacher = await app.CreateSignedInClientAsync(teacherUser, RoleNames.Learner, RoleNames.Teacher);
        using var student = await app.CreateSignedInClientAsync(studentUser, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            var preset = new RulePreset { Name = "Kelas " + TestData.Unique(), OwnerUserId = teacherUser.Id, TimeLimitMinutes = 30, MaxAttempts = 2, ShuffleQuestions = false, ShuffleOptions = false };
            examId = (await db.SaveAsync(TestData.Exam().OwnedBy(teacherUser).WithRules(preset).WithQuestions(3).Build())).Id;
        }

        // Create the class.
        using var created = await teacher.PostFormAsync("/learn/kelas", new Dictionary<string, string> { ["Name"] = "Bimbel SKD Batch 3" }, "/learn/kelas?handler=Create");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var classUrl = created.Headers.Location!.OriginalString;
        var classId = long.Parse(classUrl.Split('/')[^1], CultureInfo.InvariantCulture);
        string code;
        await using (var db = database.CreateContext())
        {
            code = (await db.Classrooms.SingleAsync(c => c.Id == classId)).InviteCode;
        }

        Assert.Matches("^[2-9A-HJ-NP-Z]{8}$", code);

        // Assign the teacher's exam for the coming week (Jakarta time).
        var opens = JakartaTime.ToLocal(app.Clock.UtcNow).AddMinutes(-5);
        using var assigned = await teacher.PostFormAsync(classUrl, new Dictionary<string, string>
        {
            ["Assign.ExamId"] = examId.ToString(CultureInfo.InvariantCulture),
            ["Assign.OpensAt"] = opens.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
            ["Assign.ClosesAt"] = opens.AddDays(7).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
        }, $"{classUrl}?handler=Assign");
        Assert.Equal(HttpStatusCode.Redirect, assigned.StatusCode);
        ClassAssignment assignment;
        await using (var db = database.CreateContext())
        {
            assignment = await db.ClassAssignments.Include(a => a.RulePreset).SingleAsync(a => a.ClassroomId == classId);
        }

        Assert.Equal((examId, 30, 2, teacherUser.Id), (assignment.ExamId, assignment.RulePreset!.TimeLimitMinutes, assignment.RulePreset.MaxAttempts, assignment.RulePreset.OwnerUserId!.Value));

        // The student joins with the code typed in lower case, and starts the assignment as a Simulation.
        using var joined = await student.PostFormAsync("/learn/kelas/gabung", new Dictionary<string, string> { ["kode"] = code.ToLowerInvariant() });
        Assert.Equal(classUrl, joined.Headers.Location!.OriginalString);
        using var started = await student.PostFormAsync(classUrl, new Dictionary<string, string>
        {
            ["assignmentId"] = assignment.Id.ToString(CultureInfo.InvariantCulture), ["examId"] = examId.ToString(CultureInfo.InvariantCulture),
        }, $"{classUrl}?handler=Start");
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var player = started.Headers.Location!.OriginalString;
        Assert.StartsWith("/sesi/", player, StringComparison.Ordinal);
        using (await student.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        await using (var db = database.CreateContext())
        {
            var attempt = await db.Attempts.SingleAsync(a => a.ClassAssignmentId == assignment.Id);
            Assert.Equal((AttemptMode.Simulation, studentUser.Id, AttemptStatus.Submitted), (attempt.Mode, attempt.UserId!.Value, attempt.Status));
            Assert.True(await db.UserBadges.AnyAsync(b => b.UserId == studentUser.Id && b.Code == BadgeCode.ClassJoined));
        }

        // The teacher sees the student's result and the class leaderboard.
        var results = await teacher.GetStringAsync(new Uri($"{classUrl}/tugas/{assignment.Id}", UriKind.Relative));
        Assert.Contains("data-testid=\"student-results\"", results, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"attempt-count\">1<", results, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"leaderboard-name\">Murid Satu<", results, StringComparison.Ordinal);
        var studentView = await student.GetStringAsync(new Uri($"{classUrl}/tugas/{assignment.Id}", UriKind.Relative));
        Assert.DoesNotContain("data-testid=\"student-results\"", studentView, StringComparison.Ordinal);
        Assert.Contains("data-viewer=\"true\"", studentView, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_removed_student_loses_access_and_cannot_rejoin()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var teacherUser = TestData.User().Build();
        var studentUser = TestData.User().Build();
        using var teacher = await app.CreateSignedInClientAsync(teacherUser, RoleNames.Learner, RoleNames.Teacher);
        using var student = await app.CreateSignedInClientAsync(studentUser, RoleNames.Learner);
        long examId, classId, assignmentId;
        string code;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().OwnedBy(teacherUser).WithQuestions(2).Build());
            examId = exam.Id;
            var classroom = new Classroom { TeacherUserId = teacherUser.Id, Name = "Kelas A", InviteCode = Unique8(), CreatedAt = TestData.Now };
            classroom.Members.Add(new ClassMember { UserId = studentUser.Id, JoinedAt = TestData.Now });
            var assignment = new ClassAssignment
            {
                Classroom = classroom, ExamId = exam.Id, RulePresetId = exam.RulePresetId,
                OpensAt = app.Clock.UtcNow.AddHours(-1), ClosesAt = app.Clock.UtcNow.AddDays(1),
            };
            db.ClassAssignments.Add(assignment);
            await db.SaveChangesAsync();
            (classId, assignmentId, code) = (classroom.Id, assignment.Id, classroom.InviteCode);
        }

        var url = $"/learn/kelas/{classId}";
        using var before = await student.GetAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative));
        using var removed = await teacher.PostFormAsync(url, new Dictionary<string, string> { ["studentId"] = studentUser.Id.ToString() }, $"{url}?handler=Remove");
        using var page = await student.GetAsync(new Uri(url, UriKind.Relative));
        using var setup = await student.GetAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative));
        using var rejoin = await student.PostFormAsync("/learn/kelas/gabung", new Dictionary<string, string> { ["kode"] = code });

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rejoin.StatusCode);
        Assert.Contains("data-testid=\"error-code\"", await rejoin.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.NotNull((await check.ClassMembers.SingleAsync(m => m.ClassroomId == classId && m.UserId == studentUser.Id)).RemovedAt);
        Assert.Equal(assignmentId, (await check.ClassAssignments.SingleAsync(a => a.ClassroomId == classId)).Id);
    }

    [Fact]
    public async Task Only_teachers_create_classes_and_closed_assignments_cannot_start()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var teacherUser = TestData.User().Build();
        var learnerUser = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(learnerUser, RoleNames.Learner);
        long classId, assignmentId, examId;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(teacherUser);
            var exam = await db.SaveAsync(TestData.Exam().OwnedBy(teacherUser).WithQuestions(2).Build());
            examId = exam.Id;
            var classroom = new Classroom { TeacherUserId = teacherUser.Id, Name = "Kelas B", InviteCode = Unique8(), CreatedAt = TestData.Now };
            classroom.Members.Add(new ClassMember { UserId = learnerUser.Id, JoinedAt = TestData.Now });
            var assignment = new ClassAssignment { Classroom = classroom, ExamId = exam.Id, RulePresetId = exam.RulePresetId, OpensAt = app.Clock.UtcNow.AddHours(1), ClosesAt = app.Clock.UtcNow.AddHours(2) };
            db.ClassAssignments.Add(assignment);
            await db.SaveChangesAsync();
            (classId, assignmentId) = (classroom.Id, assignment.Id);
        }

        var index = await learner.GetStringAsync(new Uri("/learn/kelas", UriKind.Relative));
        using var create = await learner.PostFormAsync("/learn/kelas", new Dictionary<string, string> { ["Name"] = "Kelas saya" }, "/learn/kelas?handler=Create");
        var url = $"/learn/kelas/{classId}";
        using var early = await learner.PostFormAsync(url, new Dictionary<string, string>
        {
            ["assignmentId"] = assignmentId.ToString(CultureInfo.InvariantCulture), ["examId"] = examId.ToString(CultureInfo.InvariantCulture),
        }, $"{url}?handler=Start");

        Assert.Contains("data-testid=\"teacher-hint\"", index, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(url, early.Headers.Location!.OriginalString);
        await using var check = database.CreateContext();
        Assert.False(await check.Classrooms.AnyAsync(c => c.TeacherUserId == learnerUser.Id));
        Assert.False(await check.Attempts.AnyAsync(a => a.ClassAssignmentId == assignmentId));
    }

    private static string Unique8() => TestData.Unique()[..8].ToUpperInvariant();
}
