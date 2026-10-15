using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TourSearch.Core.Search;

/// <summary>
/// Computes <c>SearchJob.ParametersHash</c>: <c>sha256:</c> followed by the lowercase hexadecimal
/// SHA-256 of the <b>canonical JSON</b> of a <see cref="SearchRequest"/>
/// (docs/domain.md#identity-and-dedupe, docs/adr/012-no-idempotency-key.md).
/// </summary>
/// <remarks>
/// <para>
/// The canonical form is built <b>explicitly</b> — one sorted key per parameter, one formatting rule
/// per value — and never by serialising the request object with a default serialiser. Property
/// order and culture leakage are exactly the two ways the same logical request would produce two
/// different hashes, and a dedupe that silently stops working is invisible in review. Changing this
/// form invalidates every stored hash and causes a wave of duplicate jobs, which is why the tests
/// pin the canonical JSON and the hash it yields.
/// </para>
/// <para>
/// The rules: keys sorted with the ordinal comparer; <c>start_date</c> as ISO-8601 day precision
/// (<c>yyyy-MM-dd</c>) with the invariant calendar, so a non-Gregorian current culture cannot change
/// the year; numbers formatted invariantly, never with a thousands separator or a decimal comma;
/// <c>currency</c> always present and explicit, even when the caller relied on the default; an
/// absent price bound contributes JSON <c>null</c>, which is a different request from a bound of
/// <c>0</c>.
/// </para>
/// </remarks>
public static class ParameterHasher
{
    /// <summary>The documented prefix of the stored hash.</summary>
    public const string Prefix = "sha256:";

    /// <summary>
    /// Length of the stored hash: the prefix plus 64 lowercase hexadecimal characters — the width of
    /// the <c>parameters_hash</c> column.
    /// </summary>
    public const int HashLength = PrefixLength + 64;

    /// <summary>Characters in <see cref="Prefix">sha256:</see> — a <c>const</c> cannot read it.</summary>
    private const int PrefixLength = 7;

    private const string DateOnlyFormat = "yyyy-MM-dd";

    /// <summary>Computes the canonical hash of a validated request.</summary>
    /// <param name="request">The request to canonicalise.</param>
    /// <returns>The hash, e.g. <c>sha256:f36a5aba…</c>, always <see cref="HashLength"/> characters long.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static string ComputeHash(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bytes = Encoding.UTF8.GetBytes(CanonicalJson(request));

        return Prefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>Builds the canonical JSON the hash is computed over.</summary>
    /// <param name="request">The request to canonicalise.</param>
    /// <returns>
    /// One compact line, keys in ordinal order, e.g.
    /// <c>{"adults":2,"children":0,"currency":"RUB","hotel_stars":0,"nights":7,"price_max":null,"price_min":800.50,"region":"kemer","start_date":"2026-06-15"}</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Exposed for tests and for a human diffing two requests: the hash is opaque, the canonical
    /// form is not. Do not parse it as data — it exists to be hashed.
    /// </remarks>
    public static string CanonicalJson(SearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A SortedDictionary, not a JsonObject built in the "right" order: the ordering is then a
        // property of the type rather than a property of the call site someone will later refactor.
        var canonical = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal)
        {
            ["adults"] = JsonValue.Create(request.Adults),
            ["children"] = JsonValue.Create(request.Children),
            ["currency"] = JsonValue.Create(request.Currency),
            ["hotel_stars"] = JsonValue.Create(request.HotelStars),
            ["nights"] = JsonValue.Create(request.Nights),

            // Absent stays null, so "no upper bound" and "at most 0" are different requests.
            ["price_max"] = request.PriceMax is { } priceMax ? JsonValue.Create(priceMax) : null,
            ["price_min"] = request.PriceMin is { } priceMin ? JsonValue.Create(priceMin) : null,
            ["region"] = JsonValue.Create(request.Region),

            // The date only, in the invariant (Gregorian) calendar: with a non-Gregorian
            // current culture "yyyy-MM-dd" would render a different year for the same instant.
            ["start_date"] = JsonValue.Create(
                request.StartDate.ToString(DateOnlyFormat, CultureInfo.InvariantCulture)),
        };

        // System.Text.Json always writes numbers invariantly; the culture-sensitive values above are
        // already strings by now, which is what keeps the whole form culture-independent.
        return JsonSerializer.Serialize(canonical);
    }
}
