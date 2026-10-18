namespace Akusiap.Domain.Users;

public static class RoleNames
{
    public const string Learner = "Learner";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";

    public static IReadOnlyList<string> All { get; } = [Learner, Teacher, Admin];
}
