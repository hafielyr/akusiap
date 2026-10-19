using Akusiap.TestSupport.Database;

namespace Akusiap.IntegrationTests;

/// <summary>Tests in this collection share one migrated database per run (D-15) and run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class UsesDatabase : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Database";
}
