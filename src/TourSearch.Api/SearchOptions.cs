using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TourSearch.Api;

/// <summary>
/// The tuning knobs of <c>search_tours</c>: how old a reusable result may be, how many jobs may be
/// in flight at once, and the poll hint handed to the caller.
/// </summary>
/// <remarks>
/// <para>
/// All three are settings rather than constants compiled into the tool, because each one tracks
/// something outside the code: freshness tracks how stale a scraped result may be before it stops
/// being useful (ADR 047), the active-job limit tracks the Worker's throughput, and
/// <c>poll_after_ms</c> should follow the real pickup latency.
/// </para>
/// <para>
/// The defaults here are the only defaults: a missing key means the documented value, an unparsable
/// one fails startup naming the key. They are duplicated in <c>appsettings.json</c> on purpose, so
/// the deployed values are readable without reading this file.
/// </para>
/// </remarks>
/// <param name="ReuseWindow">
/// How long a <c>Completed</c> job stays reusable. Not the job TTL: retention and freshness are
/// different questions (docs/adr/047-reuse-window-separate-from-ttl.md).
/// </param>
/// <param name="ActiveJobLimit">Maximum number of jobs in flight (<c>Pending</c> + <c>InProgress</c>).</param>
/// <param name="PollAfterMs">The <c>poll_after_ms</c> returned to the caller.</param>
public sealed record SearchOptions(TimeSpan ReuseWindow, int ActiveJobLimit, int PollAfterMs)
{
    /// <summary>Configuration key of the reuse window, in hours.</summary>
    public const string ReuseWindowHoursKey = "Search:ReuseWindowHours";

    /// <summary>Configuration key of the active-job limit.</summary>
    public const string ActiveJobLimitKey = "Search:ActiveJobLimit";

    /// <summary>Configuration key of the poll hint, in milliseconds.</summary>
    public const string PollAfterMsKey = "Search:PollAfterMs";

    /// <summary>Documented default of <see cref="ReuseWindowHoursKey"/>.</summary>
    public const int DefaultReuseWindowHours = 24;

    /// <summary>Documented default of <see cref="ActiveJobLimitKey"/>.</summary>
    public const int DefaultActiveJobLimit = 10;

    /// <summary>Documented default of <see cref="PollAfterMsKey"/>.</summary>
    public const int DefaultPollAfterMs = 5000;

    /// <summary>Reads and validates the settings.</summary>
    /// <param name="configuration">The configuration of the process (appsettings.json + environment).</param>
    /// <returns>The settings, with the documented default for every key that is absent.</returns>
    /// <exception cref="InvalidOperationException">
    /// A key is present but is not a positive integer. The message names the key, so a misconfigured
    /// start is diagnosable instead of silently serving a nonsense limit
    /// (docs/runtime.md#startup).
    /// </exception>
    public static SearchOptions FromConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var reuseWindowHours = PositiveOrDefault(configuration, ReuseWindowHoursKey, DefaultReuseWindowHours);
        var activeJobLimit = PositiveOrDefault(configuration, ActiveJobLimitKey, DefaultActiveJobLimit);
        var pollAfterMs = PositiveOrDefault(configuration, PollAfterMsKey, DefaultPollAfterMs);

        return new SearchOptions(
            TimeSpan.FromHours(reuseWindowHours),
            activeJobLimit,
            pollAfterMs);
    }

    private static int PositiveOrDefault(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1)
        {
            throw new InvalidOperationException(
                $"Configuration value {key} must be a positive integer (found '{value}'). It is read " +
                "from appsettings.json or from the environment, and search_tours refuses to start " +
                "with a nonsensical limit (docs/runtime.md#startup).");
        }

        return parsed;
    }
}
