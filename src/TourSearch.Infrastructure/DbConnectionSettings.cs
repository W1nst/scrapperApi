using System.Globalization;
using Npgsql;

namespace TourSearch.Infrastructure;

/// <summary>
/// The database connection settings of TourSearch, read from the <c>TOURSEARCH_DB_*</c>
/// environment variables.
/// </summary>
/// <remarks>
/// <para>
/// The environment is the only source both consumers can share: the runtime graph
/// (<see cref="DependencyInjection.AddInfrastructure"/>) and the design-time factory, which
/// <c>dotnet ef</c> runs in a <b>separate process</b> that sees neither the application's DI
/// graph nor its secret provider (docs/runtime.md#configuration-and-secrets).
/// </para>
/// <para>
/// The password is held in memory only. It is never logged, never written to a file, and never
/// exposed by a public member of this type (ADR 003, docs/conventions.md#configuration).
/// </para>
/// </remarks>
public sealed class DbConnectionSettings
{
    /// <summary>Required. Host of the PostgreSQL server.</summary>
    public const string HostVariable = "TOURSEARCH_DB_HOST";

    /// <summary>Required. Port of the PostgreSQL server.</summary>
    public const string PortVariable = "TOURSEARCH_DB_PORT";

    /// <summary>
    /// Required. Database name. The database itself must already exist: nothing here creates or
    /// drops it, and migrations are applied by an explicit step (docs/database.md#migrations).
    /// </summary>
    public const string NameVariable = "TOURSEARCH_DB_NAME";

    /// <summary>Required. Login role.</summary>
    public const string UserVariable = "TOURSEARCH_DB_USER";

    /// <summary>
    /// Required. Password of the login role. Never committed, never echoed (ADR 003).
    /// </summary>
    public const string PasswordVariable = "TOURSEARCH_DB_PASSWORD";

    private readonly string _password;

    private DbConnectionSettings(string host, int port, string database, string user, string password)
    {
        Host = host;
        Port = port;
        Database = database;
        User = user;
        _password = password;
    }

    /// <summary>Host of the PostgreSQL server.</summary>
    public string Host { get; }

    /// <summary>Port of the PostgreSQL server.</summary>
    public int Port { get; }

    /// <summary>Database name.</summary>
    public string Database { get; }

    /// <summary>Login role.</summary>
    public string User { get; }

    /// <summary>
    /// Reads the five required variables from the process environment and validates them.
    /// </summary>
    /// <returns>The settings for this process.</returns>
    /// <exception cref="InvalidOperationException">
    /// A required variable is missing or blank, or <see cref="PortVariable"/> is not a valid
    /// port. The message names the variable, so a misconfigured start is diagnosable:
    /// startup must fail fast, not half-start and then loop (docs/runtime.md#startup).
    /// </exception>
    public static DbConnectionSettings FromEnvironment()
    {
        var portText = Required(PortVariable);

        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidOperationException(
                $"Environment variable {PortVariable} must be an integer between 1 and 65535.");
        }

        return new DbConnectionSettings(
            Required(HostVariable),
            port,
            Required(NameVariable),
            Required(UserVariable),
            Required(PasswordVariable));
    }

    /// <summary>
    /// Builds a pooled data source for these settings.
    /// </summary>
    /// <returns>
    /// A data source. It is shared state and is registered as a <c>Singleton</c> in the
    /// application graph (ADR 015, docs/database.md#connection-pool).
    /// </returns>
    /// <remarks>
    /// The connection string is assembled here, in memory, from the discrete variables — no
    /// single string containing a credential is ever written to a file, and the data source
    /// does not expose it. Building a data source opens no connection.
    /// </remarks>
    public NpgsqlDataSource CreateDataSource()
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = User,
            Password = _password,
        };

        return new NpgsqlDataSourceBuilder(connectionString.ConnectionString).Build();
    }

    /// <summary>Describes the target and the role — deliberately without the password.</summary>
    /// <returns>A log-safe description, e.g. <c>127.0.0.1:5432/tour_search as postgres</c>.</returns>
    public override string ToString() => $"{Host}:{Port}/{Database} as {User}";

    private static string Required(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable {variable} is not set. TourSearch reads its " +
                "database connection settings from the TOURSEARCH_DB_* environment variables and " +
                "refuses to start with an incomplete configuration (docs/runtime.md#startup).");
        }

        return value;
    }
}
