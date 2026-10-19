using Akusiap.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>Compares a migrated database with schema.sql.</summary>
internal static class SchemaAssertions
{
    /// <summary>
    /// Expected columns per table as "column type", with "?" for nullable and PostgreSQL udt names
    /// (int8, text, timestamptz …). Returns one line per missing or different column.
    /// </summary>
    public static async Task<List<string>> FindColumnProblemsAsync(AppDbContext db, IReadOnlyDictionary<string, string[]> expected)
    {
        var actual = await db.Database
            .SqlQueryRaw<ColumnInfo>(
                "SELECT table_name AS \"Table\", column_name AS \"Column\", udt_name AS \"Type\", is_nullable = 'YES' AS \"Nullable\" " +
                "FROM information_schema.columns WHERE table_schema = 'public'")
            .ToListAsync();
        var columns = actual.ToDictionary(c => (c.Table, c.Column));

        var problems = new List<string>();
        foreach (var (table, specs) in expected)
        {
            foreach (var spec in specs)
            {
                var parts = spec.Split(' ');
                var nullable = parts[1].EndsWith('?');
                var type = parts[1].TrimEnd('?');
                if (!columns.TryGetValue((table, parts[0]), out var column))
                {
                    problems.Add($"{table}.{parts[0]} is missing");
                }
                else if (column.Type != type || column.Nullable != nullable)
                {
                    problems.Add($"{table}.{parts[0]} is {column.Type}{(column.Nullable ? "?" : "")}, expected {parts[1]}");
                }
            }
        }

        return problems;
    }

    /// <summary>True when <paramref name="table"/> has a unique index or key over exactly <paramref name="columns"/> (as in its definition).</summary>
    public static async Task<bool> HasUniqueIndexAsync(AppDbContext db, string table, string columns)
    {
        var definitions = await db.Database
            .SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE schemaname = 'public' AND tablename = {0} AND indexdef LIKE 'CREATE UNIQUE%'",
                table)
            .ToListAsync();
        return definitions.Any(d => d.Contains($"({columns})", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs each statement and returns a line for every one that was accepted, or rejected by a different constraint.
    /// A failed statement changes nothing, so the cases don't affect each other.
    /// </summary>
    public static async Task<List<string>> FindUnenforcedRulesAsync(AppDbContext db, IEnumerable<(string Sql, string Constraint)> cases)
    {
        var problems = new List<string>();
        foreach (var (sql, constraint) in cases)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                problems.Add($"accepted: {sql}");
            }
            catch (PostgresException ex) when (ex.ConstraintName != constraint)
            {
                problems.Add($"{sql} broke {ex.ConstraintName ?? ex.SqlState}, expected {constraint}");
            }
            catch (PostgresException)
            {
                // Rejected by the expected constraint.
            }
        }

        return problems;
    }

    private sealed record ColumnInfo(string Table, string Column, string Type, bool Nullable);
}
