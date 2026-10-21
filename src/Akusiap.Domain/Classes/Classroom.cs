using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Classes;

/// <summary>A teacher's class (stories T-09, T-10). Students join with the invite code.</summary>
public class Classroom
{
    public const int InviteCodeLength = 8;

    /// <summary>Uppercase letters and digits without look-alikes (0/O, 1/I/L), so codes are easy to read aloud.</summary>
    public const string InviteCodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public long Id { get; set; }

    public Guid TeacherUserId { get; set; }

    public User? TeacherUser { get; set; }

    public string Name { get; set; } = string.Empty;

    public string InviteCode { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public ICollection<ClassMember> Members { get; } = [];

    public ICollection<ClassAssignment> Assignments { get; } = [];

    public static string NewInviteCode(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return string.Create(InviteCodeLength, random, (span, r) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = InviteCodeAlphabet[r.Next(InviteCodeAlphabet.Length)];
            }
        });
    }
}

/// <summary>A student in a class. Removing sets <see cref="RemovedAt"/> (T-13); the row stays for past results.</summary>
public class ClassMember
{
    public long ClassroomId { get; set; }

    public Classroom? Classroom { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public DateTime JoinedAt { get; set; }

    public DateTime? RemovedAt { get; set; }
}

/// <summary>
/// An exam assigned to a class with an open window (T-11). The rule preset is a copy made at assignment time, so the
/// rules are locked for the class even if the exam's own rules change later (BR-15, T-05).
/// </summary>
public class ClassAssignment
{
    public long Id { get; set; }

    public long ClassroomId { get; set; }

    public Classroom? Classroom { get; set; }

    public long ExamId { get; set; }

    public Exam? Exam { get; set; }

    public long RulePresetId { get; set; }

    public RulePreset? RulePreset { get; set; }

    public DateTime OpensAt { get; set; }

    public DateTime ClosesAt { get; set; }
}
