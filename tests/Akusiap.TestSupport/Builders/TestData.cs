using Akusiap.Infrastructure.Persistence;

namespace Akusiap.TestSupport.Builders;

/// <summary>
/// Entry point for test data, for example <c>TestData.Exam().Paid(49000).WithQuestions(40, trial: 30)</c>.
/// Builders return unsaved entity graphs with unique names, so tests can share one database.
/// </summary>
public static class TestData
{
    /// <summary>Times used when a builder needs one; matches the default <see cref="Fakes.TestClock"/> start.</summary>
    public static readonly DateTime Now = Fakes.TestClock.DefaultStart;

    public static ExamBuilder Exam() => new();

    public static UserBuilder User() => new();

    /// <summary>A short unique suffix for names that must not collide.</summary>
    public static string Unique() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>Adds the built graph to <paramref name="db"/> and saves it.</summary>
    public static async Task<T> SaveAsync<T>(this AppDbContext db, T entity)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(db);
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }
}
