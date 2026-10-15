using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using TourSearch.Infrastructure;

namespace TourSearch.UnitTests.Infrastructure;

/// <summary>
/// Guards that the model the runtime builds and the migration snapshot agree: the migration
/// exists, is named by meaning, and has no pending differences from the live model (FR-003,
/// NFR-001).
/// </summary>
/// <remarks>
/// The check is the in-memory equivalent of
/// <c>dotnet ef migrations has-pending-model-changes</c> — it compares the snapshot model with
/// the design-time model through <see cref="IMigrationsModelDiffer"/> and needs no server.
/// <c>Database.GetPendingMigrations()</c> is deliberately not used: it asks the database which
/// migrations are applied, and no test in this project contacts a database
/// (docs/conventions.md#testing).
/// </remarks>
[TestClass]
public sealed class MigrationSnapshotTests
{
    private const string PlaceholderConnectionString = "Host=localhost;Database=toursearch";

    [TestMethod]
    public void Migrations_WhenLoaded_ContainExactlyOneMigrationNamedByMeaning()
    {
        using var dataSource = CreateDataSource();
        using var context = CreateContext(dataSource);

        var migrations = context.Database.GetMigrations().ToList();

        Assert.HasCount(1, migrations, $"Expected one migration: {string.Join(", ", migrations)}");
        StringAssert.EndsWith(
            migrations[0],
            "_InitialSchema",
            "A migration is named by meaning, never Migration1 (docs/database.md#migrations).");
    }

    [TestMethod]
    public void Model_WhenComparedWithTheMigrationSnapshot_HasNoPendingDifferences()
    {
        using var dataSource = CreateDataSource();
        using var context = CreateContext(dataSource);

        var snapshotModel = context.GetService<IMigrationsAssembly>().ModelSnapshot?.Model;
        Assert.IsNotNull(
            snapshotModel,
            "No model snapshot: the migrations were not added through `dotnet ef migrations add`.");

        // The snapshot is the output of a stand-alone model builder, so it is neither finalized
        // nor have its runtime dependencies been initialized yet. The differ needs both, exactly
        // as the design-time tools do it.
        var initializedSnapshotModel = context.GetService<IModelRuntimeInitializer>()
            .Initialize(snapshotModel, designTime: true, validationLogger: null);

        var modelDiffer = context.GetService<IMigrationsModelDiffer>();
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        Assert.IsFalse(
            modelDiffer.HasDifferences(
                initializedSnapshotModel.GetRelationalModel(),
                designTimeModel.GetRelationalModel()),
            "The model has changed since the last migration. Run `dotnet ef migrations add <Name> " +
            "--project src/TourSearch.Infrastructure.Migration` — the snapshot and the runtime model " +
            "must agree, or the applied schema is not the model (ADR 002, ADR 040).");
    }

    /// <summary>
    /// A data source built from a credential-free placeholder: this test only needs a model, and
    /// no connection is opened until a query runs.
    /// </summary>
    private static NpgsqlDataSource CreateDataSource() =>
        NpgsqlDataSource.Create(PlaceholderConnectionString);

    private static TourSearchDbContext CreateContext(NpgsqlDataSource dataSource) =>
        new(
            // The shared factory is the one path that supplies the provider and the snake_case
            // naming (NFR-001), plus the migrations assembly that holds the snapshot.
            TourSearchDbContextOptionsFactory.Create(
                dataSource,
                npgsql => npgsql.MigrationsAssembly("TourSearch.Infrastructure.Migration")));
}
