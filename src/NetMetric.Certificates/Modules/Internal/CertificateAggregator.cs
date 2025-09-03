// <copyright file="CertificateAggregator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Modules.Internal;

/// <summary>
/// Represents a single aggregated certificate entry with derived fields.
/// </summary>
/// <param name="Cert">The underlying certificate metadata.</param>
/// <param name="DaysLeft">
/// Days remaining until certificate expiry. May be negative when the certificate is already expired.
/// </param>
/// <param name="Severity">
/// A severity label derived from <paramref name="DaysLeft"/> using the warning/critical thresholds in
/// <see cref="CertificatesOptions"/>.
/// </param>
public readonly record struct Item(CertificateInfo Cert, double DaysLeft, string Severity);

/// <summary>
/// Aggregation statistics for a snapshot produced by <see cref="CertificateAggregator"/>.
/// </summary>
/// <param name="NowUtc">The UTC timestamp when the snapshot was computed.</param>
/// <param name="SourceCount">The number of sources successfully enumerated.</param>
/// <param name="Errors">The number of source-level errors encountered (swallowed).</param>
/// <param name="Total">The total number of de-duplicated certificates in the resulting snapshot.</param>
public readonly record struct Stats(DateTime NowUtc, int SourceCount, int Errors, int Total);

/// <summary>
/// Aggregates certificate data from multiple <see cref="ICertificateSource"/> instances,
/// applies de-duplication and simple error tolerance, and exposes a cached snapshot
/// with computed derived fields (e.g., days left until expiry and severity).
/// </summary>
/// <remarks>
/// <para>
/// The aggregator enumerates all configured sources concurrently (optionally throttled via
/// <see cref="CertificatesOptions.MaxConcurrentSources"/>) and de-duplicates certificates by
/// <see cref="CertificateInfo.Id"/>, keeping the entry with the earliest <c>NotAfterUtc</c>.
/// </para>
/// <para>
/// Results are cached for a bounded time window (TTL) defined by <see cref="CertificatesOptions.ScanTtl"/>.
/// If the TTL is not provided or non-positive, a default of 30 seconds is used. Repeated calls within the TTL
/// return the cached snapshot without re-scanning sources.
/// </para>
/// <para>
/// Source-level exceptions are swallowed and recorded in the returned <see cref="Stats"/> to allow
/// partial progress while preventing a single failing source from breaking the whole aggregation.
/// Cancellation is honored via the provided <see cref="CancellationToken"/>.
/// </para>
/// <para><b>Thread-safety</b><br/>
/// Each snapshot call is independent. A minimal in-memory cache is maintained per instance. If you share
/// a single <see cref="CertificateAggregator"/> across collectors, do so as a singleton to reuse the cache.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Configure sources (e.g., Windows store + explicit files):
/// IEnumerable<ICertificateSource> sources = new ICertificateSource[]
/// {
///     new X509StoreCertificateSource(),                          // CurrentUser/My
///     new FileCertificateSource(@"C:\certs\gateway.pfx")         // File-based source
/// };
///
/// // Options: scan TTL, concurrency and severity thresholds
/// var opt = new CertificatesOptions
/// {
///     ScanTtl = TimeSpan.FromSeconds(45),
///     MaxConcurrentSources = 4,
///     WarningDays = 30,
///     CriticalDays = 7
/// };
///
/// var agg = new CertificateAggregator(sources, opt);
///
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
/// var (items, stats) = await agg.GetSnapshotAsync(cts.Token);
///
/// // items: de-duplicated certificates with DaysLeft and Severity ("ok" | "warning" | "critical")
/// // stats: timing and counts (source count, errors, total)
/// ]]></code>
/// </example>
/// <seealso cref="ICertificateSource"/>
/// <seealso cref="CertificatesOptions"/>
public sealed class CertificateAggregator
{
    private readonly IEnumerable<ICertificateSource> _sources;
    private readonly CertificatesOptions _opt;

    /// <summary>
    /// In-memory cache of the last snapshot. When <see cref="CertificatesOptions.ScanTtl"/> has not elapsed,
    /// <see cref="GetSnapshotAsync(System.Threading.CancellationToken)"/> returns this cached value.
    /// </summary>
    private (DateTime tsUtc, ImmutableArray<Item> items, Stats stats) _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="CertificateAggregator"/> class.
    /// </summary>
    /// <param name="sources">The collection of certificate sources to enumerate.</param>
    /// <param name="opt">Behavioral options such as TTL, throttling and severity thresholds.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="sources"/> or <paramref name="opt"/> is <see langword="null"/>.
    /// </exception>
    public CertificateAggregator(IEnumerable<ICertificateSource> sources, CertificatesOptions opt)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _opt = opt ?? throw new ArgumentNullException(nameof(opt));
    }

    /// <summary>
    /// Produces a snapshot of aggregated certificate data, honoring caching and cancellation.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>
    /// A tuple of <c>items</c> and <see cref="Stats"/> where:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>items</c> is the de-duplicated set of certificates with computed
    ///       <see cref="Item.DaysLeft"/> (in days) and a severity label computed via
    ///       <see cref="Severity.FromDaysLeft(double,double,double)"/>.
    ///     </description>
    ///   </item>
    ///   <item><description><c>stats</c> summarizes timing and aggregation metrics (source count, errors, total items).</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// If a warm cache exists and the elapsed time since the cached timestamp is within the configured TTL,
    /// the cached snapshot is returned. Otherwise, all sources are scanned:
    /// </para>
    /// <list type="number">
    ///   <item>Optionally throttle concurrency using <see cref="CertificatesOptions.MaxConcurrentSources"/>.</item>
    ///   <item>For each emitted <see cref="CertificateInfo"/>, keep the earliest expiring instance per <see cref="CertificateInfo.Id"/>.</item>
    ///   <item>Compute days left as <c>(NotAfterUtc - now).TotalDays</c> and map to severity via <see cref="Severity.FromDaysLeft(double,double,double)"/> using warning/critical thresholds from options.</item>
    ///   <item>Populate <see cref="Stats"/> with the scan time (<see cref="Stats.NowUtc"/>), number of polled sources, error count, and total certificates.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    public async Task<(IReadOnlyList<Item> items, Stats stats)> GetSnapshotAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var ttl = _opt.ScanTtl <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : _opt.ScanTtl;

        if (_cache.items.Length > 0 && now - _cache.tsUtc <= ttl)
            return (_cache.items, _cache.stats);

        var dict = new ConcurrentDictionary<string, CertificateInfo>(StringComparer.Ordinal);
        int srcCount = 0, err = 0;

        async Task ProcessAsync(ICertificateSource s, SemaphoreSlim? throttler)
        {
            try
            {
                if (throttler is not null)
                    await throttler.WaitAsync(ct).ConfigureAwait(false);

                Interlocked.Increment(ref srcCount);

                await foreach (var c in s.EnumerateAsync(ct).WithCancellation(ct))
                {
                    ct.ThrowIfCancellationRequested();

                    dict.AddOrUpdate(
                        c.Id,
                        c,
                        (_, old) => c.NotAfterUtc < old.NotAfterUtc ? c : old
                    );
                }
            }
            catch (OperationCanceledException)
            {
                // Honor cancellation: rethrow so the overall operation is canceled.
                throw;
            }
            // Swallow only expected, known exception kinds from sources.
            catch (Exception ex) when (
                ex is IOException ||
                ex is CryptographicException ||
                ex is InvalidOperationException ||
                ex is NotSupportedException ||
                ex is ArgumentException
            )
            {
                Interlocked.Increment(ref err);
            }
            finally
            {
                throttler?.Release();
            }
        }

        if (_opt.MaxConcurrentSources is { } k && k > 0)
        {
            using var throttler = new SemaphoreSlim(k, k);
            var tasks = _sources.Select(s => ProcessAsync(s, throttler));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        else
        {
            var tasks = _sources.Select(s => ProcessAsync(s, null));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        var items = dict.Values.Select(c =>
        {
            var days = (c.NotAfterUtc - now).TotalDays;
            var sev = Severity.FromDaysLeft(days, _opt.WarningDays, _opt.CriticalDays);
            return new Item(c, days, sev);
        }).ToImmutableArray();

        var stats = new Stats(now, srcCount, err, items.Length);
        _cache = (now, items, stats);
        return (items, stats);
    }
}

// NOTE: This is only the signature used by XML cref. Real implementation should exist elsewhere.
internal static class Severity
{
    /// <summary>
    /// Maps remaining days to a severity label using the supplied warning/critical thresholds.
    /// </summary>
    /// <param name="daysLeft">Remaining validity in days (can be negative for expired certificates).</param>
    /// <param name="warnDays">Upper bound (inclusive) for the "warning" severity.</param>
    /// <param name="critDays">Upper bound (inclusive) for the "critical" severity.</param>
    /// <returns>
    /// <c>"critical"</c> if <paramref name="daysLeft"/> ≤ <paramref name="critDays"/>;  
    /// <c>"warning"</c> if ≤ <paramref name="warnDays"/>; otherwise <c>"ok"</c>.
    /// </returns>
    public static string FromDaysLeft(double daysLeft, double warnDays, double critDays) =>
        daysLeft <= critDays ? "critical" :
        daysLeft <= warnDays ? "warning" : "ok";
}
