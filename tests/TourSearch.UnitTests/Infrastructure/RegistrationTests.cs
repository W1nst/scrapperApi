using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TourSearch.Core.Entities;
using TourSearch.Infrastructure;

namespace TourSearch.UnitTests.Infrastructure;

/// <summary>
/// Guards the graph built by <c>AddInfrastructure()</c>: lifetimes
/// (docs/conventions.md#di, ADR 015), the absence of a hosted service (ADR 004), the fail-fast
/// configuration check (docs/runtime.md#startup), and that the context it produces still knows
/// the model.
/// </summary>
/// <remarks>
/// <para>
/// No database is contacted: registration, resolution and model inspection never open a
/// connection. That is itself the guard for NFR-003 — a <c>Migrate()</c>/<c>EnsureCreated()</c>
/// call inside <c>AddInfrastructure()</c> would need a connection and fail here.
/// </para>
/// <para>
/// The environment is process-wide state, so this class is not parallelized; every value it
/// writes is restored afterwards.
/// </para>
/// </remarks>
[TestClass]
[DoNotParallelize]
public sealed class RegistrationTests
{
    private const string TestHost = "127.0.0.1";
    private const string TestPort = "5432";
    private const string TestDatabase = "tour_search";
    private const string TestUser = "postgres";

    private readonly Dictionary<string, string?> _previousValues = new();

    /// <summary>
    /// Sets the documented variables for the tests that need a complete configuration. None of
    /// them is a real credential, and none is a literal: the password is generated per run,
    /// because a password-shaped constant in a committed file is a leak even when it is
    /// obviously fake (ADR 003).
    /// </summary>
    [TestInitialize]
    public void SetTheDocumentedVariables()
    {
        SetVariable(DbConnectionSettings.HostVariable, TestHost);
        SetVariable(DbConnectionSettings.PortVariable, TestPort);
        SetVariable(DbConnectionSettings.NameVariable, TestDatabase);
        SetVariable(DbConnectionSettings.UserVariable, TestUser);
        SetVariable(DbConnectionSettings.PasswordVariable, Guid.NewGuid().ToString("N"));
    }

    /// <summary>Puts the process environment back the way the test found it.</summary>
    [TestCleanup]
    public void RestoreTheEnvironment()
    {
        foreach (var (name, value) in _previousValues)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _previousValues.Clear();
    }

    [TestMethod]
    public void AddInfrastructure_WhenCalled_RegistersASingletonDataSourceAndAScopedContext()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();

        var dataSourceRegistrations = services.Where(d => d.ServiceType == typeof(NpgsqlDataSource)).ToList();
        Assert.HasCount(1, dataSourceRegistrations, "One pool registration: it is shared state.");
        Assert.AreEqual(
            ServiceLifetime.Singleton,
            dataSourceRegistrations[0].Lifetime,
            "The connection pool must be one shared instance for the whole graph (ADR 015).");

        var contextRegistrations = services.Where(d => d.ServiceType == typeof(TourSearchDbContext)).ToList();
        Assert.HasCount(1, contextRegistrations);
        Assert.AreEqual(
            ServiceLifetime.Scoped,
            contextRegistrations[0].Lifetime,
            "Scoped is the documented default lifetime (docs/conventions.md#di).");

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstDataSource = firstScope.ServiceProvider.GetRequiredService<NpgsqlDataSource>();
        Assert.AreSame(
            firstDataSource,
            secondScope.ServiceProvider.GetRequiredService<NpgsqlDataSource>(),
            "Two scopes must share one pool, not open two.");

        var firstContext = firstScope.ServiceProvider.GetRequiredService<TourSearchDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<TourSearchDbContext>();
        Assert.AreNotSame(firstContext, secondContext, "Each scope gets its own context.");

        Assert.AreEqual("Npgsql.EntityFrameworkCore.PostgreSQL", firstContext.Database.ProviderName);
        Assert.IsNotNull(
            firstContext.Model.FindEntityType(typeof(SearchJob)),
            "SearchJob must be reachable through the context the graph resolves.");
        Assert.IsNotNull(firstContext.SearchJobs);

        // NFR-001: the runtime graph must go through the same options chain as the design-time
        // factory. If the snake_case plugin were missing here, this column would be `Status` —
        // and the applied schema would no longer match the model (docs/database.md#conventions).
        var statusColumn = firstContext.Model
            .FindEntityType(typeof(SearchJob))!
            .FindProperty(nameof(SearchJob.Status))!
            .GetColumnName();
        Assert.AreEqual("status", statusColumn);
    }

    [TestMethod]
    public void AddInfrastructure_WhenCalledTwice_KeepsASinglePoolRegistration()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();
        services.AddInfrastructure();

        Assert.HasCount(
            1,
            services.Where(d => d.ServiceType == typeof(NpgsqlDataSource)),
            "A second call must not register a second pool.");
    }

    [TestMethod]
    public void AddInfrastructure_WhenCalled_RegistersNoHostedService()
    {
        var services = new ServiceCollection();

        services.AddInfrastructure();

        // Only the Worker starts a BackgroundService (ADR 004). The hosting abstraction is not
        // referenced by this test project, so the check is by name rather than by type.
        Assert.IsFalse(
            services.Any(d =>
                d.ServiceType.FullName?.StartsWith("Microsoft.Extensions.Hosting.", StringComparison.Ordinal) == true
                || d.ImplementationType?.FullName?.Contains("BackgroundService", StringComparison.Ordinal) == true),
            "AddInfrastructure() must not register a hosted service (ADR 004).");
    }

    [TestMethod]
    [DataRow(DbConnectionSettings.HostVariable)]
    [DataRow(DbConnectionSettings.PortVariable)]
    [DataRow(DbConnectionSettings.NameVariable)]
    [DataRow(DbConnectionSettings.UserVariable)]
    [DataRow(DbConnectionSettings.PasswordVariable)]
    public void AddInfrastructure_WhenARequiredVariableIsMissing_FailsFastNamingThatVariable(string missingVariable)
    {
        SetVariable(missingVariable, null);
        var services = new ServiceCollection();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => services.AddInfrastructure());

        StringAssert.Contains(
            exception.Message,
            missingVariable,
            "The failure must name the variable, not report a generic connection error.");
    }

    [TestMethod]
    public void AddInfrastructure_WhenThePortIsNotANumber_FailsFastNamingTheVariable()
    {
        SetVariable(DbConnectionSettings.PortVariable, "not-a-port");
        var services = new ServiceCollection();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => services.AddInfrastructure());

        StringAssert.Contains(exception.Message, DbConnectionSettings.PortVariable);
    }

    private void SetVariable(string name, string? value)
    {
        _previousValues.TryAdd(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }
}
