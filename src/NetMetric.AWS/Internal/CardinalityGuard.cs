// <copyright file="CardinalityGuard.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Amazon.CloudWatch.Model;

namespace NetMetric.AWS.Internal;

/// <summary>
/// Enforces dimension cardinality and naming rules before adding dimensions to
/// <see cref="MetricDatum"/> instances destined for Amazon CloudWatch.
/// </summary>
/// <remarks>
/// <para>
/// CloudWatch imposes strict limits on dimensions:
/// at most 10 dimensions per metric, key and value length limits, and practical
/// cardinality constraints to avoid unbounded series growth. This helper applies
/// a consistent set of guardrails defined by <see cref="CloudWatchExporterOptions"/>:
/// </para>
/// <list type="bullet">
///   <item><description>Blocks dimension keys that match any regex in
///   <see cref="CloudWatchExporterOptions.BlockedDimensionKeyPatterns"/>.</description></item>
///   <item><description>Optionally drops empty values via
///   <see cref="CloudWatchExporterOptions.DropEmptyDimensions"/>.</description></item>
///   <item><description>Trims values to <see cref="CloudWatchExporterOptions.MaxDimensionValueLength"/>.</description></item>
///   <item><description>Tracks and bounds per-key distinct values using
///   <see cref="CloudWatchExporterOptions.MaxUniqueValuesPerKey"/> (in-memory) to mitigate
///   high-cardinality explosions.</description></item>
///   <item><description>Enforces CloudWatch’s hard limit of 10 dimensions per datum.</description></item>
/// </list>
/// <para>
/// The guard is allocation-aware and thread-safe for concurrent use within a single process.
/// It keeps an in-memory map of seen values per key to make best-effort decisions about whether
/// adding a new value risks excessive cardinality. This map is process-local and non-persistent.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp">
/// var opts = new CloudWatchExporterOptions
/// {
///     DropEmptyDimensions = true,
///     MaxDimensionValueLength = 128,
///     MaxUniqueValuesPerKey = 5000,
/// };
///
/// var guard = new CardinalityGuard(opts);
/// var datum = new MetricDatum { MetricName = "netmetric.requests" };
///
/// // Will be added
/// guard.TryAddDimension(datum, "service.name", "checkout-api");
///
/// // Empty value is dropped (when DropEmptyDimensions = true)
/// guard.TryAddDimension(datum, "deployment.environment", " ");
///
/// // Overly long value is trimmed to MaxDimensionValueLength
/// guard.TryAddDimension(datum, "host.name", new string('x', 1024));
///
/// // Duplicate key is ignored
/// guard.TryAddDimension(datum, "service.name", "checkout-api");
/// </code>
/// </example>
/// <threadsafety>
/// This type is safe for concurrent use by multiple threads. The internal
/// per-key value registry uses <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>.
/// </threadsafety>
/// <seealso cref="CloudWatchExporterOptions"/>
/// <seealso cref="MetricDatum"/>
internal sealed class CardinalityGuard
{
    private readonly CloudWatchExporterOptions _opts;
    private readonly Regex[] _deny;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _seenValues
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CardinalityGuard"/> class with the specified options.
    /// </summary>
    /// <param name="opts">Exporter options that control dimension filtering and cardinality constraints.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="opts"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The constructor compiles the configured blocked key regex patterns (if any) using
    /// <see cref="RegexOptions.Compiled"/> for performant matching on hot paths.
    /// </remarks>
    public CardinalityGuard(CloudWatchExporterOptions opts)
    {
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));

        var patterns = (IEnumerable<string>?)_opts.BlockedDimensionKeyPatterns ?? Array.Empty<string>();

        _deny = patterns
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(p => new Regex(
                p,
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant))
            .ToArray();
    }

    /// <summary>
    /// Attempts to add a dimension to the given <see cref="MetricDatum"/> subject to guard rules.
    /// </summary>
    /// <param name="datum">The datum to which the dimension may be added.</param>
    /// <param name="name">The dimension key (case-sensitive, CloudWatch treats names as case-sensitive).</param>
    /// <param name="value">The dimension value; may be <see langword="null"/> or whitespace.</param>
    /// <remarks>
    /// <para>
    /// This method is best-effort and <b>never throws</b> for validation failures; it simply
    /// returns without modifying <paramref name="datum"/> when a rule is violated. Specifically:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Returns if <paramref name="datum"/> already has 10 dimensions.</description></item>
    ///   <item><description>Returns if <paramref name="name"/> is null/empty/whitespace or matches a blocked pattern.</description></item>
    ///   <item><description>Trims <paramref name="value"/>; when
    ///     <see cref="CloudWatchExporterOptions.DropEmptyDimensions"/> is <see langword="true"/>,
    ///     empty values are dropped.</description></item>
    ///   <item><description>Truncates <paramref name="value"/> to
    ///     <see cref="CloudWatchExporterOptions.MaxDimensionValueLength"/> characters.</description></item>
    ///   <item><description>Tracks distinct values per key and returns if the number of unique values
    ///     exceeds <see cref="CloudWatchExporterOptions.MaxUniqueValuesPerKey"/>. (When this option is
    ///     0, the check is disabled.)</description></item>
    ///   <item><description>Returns if a dimension with the same <paramref name="name"/> already exists
    ///     on <paramref name="datum"/>.</description></item>
    /// </list>
    /// <para>
    /// If all checks pass, a new <see cref="Dimension"/> is appended to <see cref="MetricDatum.Dimensions"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp">
    /// var d = new MetricDatum { MetricName = "netmetric.latency" };
    /// var ok1 = d.Dimensions.Count; // 0
    /// guard.TryAddDimension(d, "service.version", "1.2.3");
    /// guard.TryAddDimension(d, "user.id", "42"); // blocked by default patterns (e.g., ".*id$")
    /// var ok2 = d.Dimensions.Count; // 1
    /// </code>
    /// </example>
    public void TryAddDimension(MetricDatum datum, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(datum);

        // Enforce CloudWatch hard cap (10)
        if (datum.Dimensions.Count >= 10) return;

        // Basic key validation
        if (string.IsNullOrWhiteSpace(name)) return;

        // Blocklisted keys
        if (_deny.Length > 0 && _deny.Any(r => r.IsMatch(name))) return;

        // Normalize and optionally drop empty
        var safeVal = (value ?? string.Empty).Trim();
        if (_opts.DropEmptyDimensions && safeVal.Length == 0) return;

        // Truncate overly long values (practical default; CloudWatch hard limit is higher)
        if (safeVal.Length > _opts.MaxDimensionValueLength)
            safeVal = safeVal.Substring(0, _opts.MaxDimensionValueLength);

        // Guard against value explosion per key
        if (_opts.MaxUniqueValuesPerKey > 0)
        {
            var map = _seenValues.GetOrAdd(name, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
            map.TryAdd(safeVal, 0);

            // If value space exceeds the threshold, refuse to add the overflowing dimension.
            if (map.Count > _opts.MaxUniqueValuesPerKey)
            {
                // Current behavior: drop the dimension when overflowing.
                // When DropOnlyOverflowingKey is false in the future, callers may choose to drop the whole metric instead.
                if (_opts.DropOnlyOverflowingKey) return;
                else return;
            }
        }

        // Avoid duplicate keys on the same datum
        if (datum.Dimensions.Exists(d => string.Equals(d.Name, name, StringComparison.Ordinal)))
            return;

        // Finally add the dimension
        datum.Dimensions.Add(new Dimension { Name = name, Value = safeVal });
    }
}
