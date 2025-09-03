// <copyright file="CloudWatchExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

using Amazon.CloudWatch;
using Amazon.CloudWatch.Model;

using Microsoft.Extensions.Options;

namespace NetMetric.Export.CloudWatch.Exporters;

/// <summary>
/// Exports application metrics to <see cref="IAmazonCloudWatch"/> (Amazon CloudWatch).
/// </summary>
/// <remarks>
/// <para>
/// <b>Supported metric shapes.</b> The exporter maps NetMetric abstractions to CloudWatch types as follows:
/// </para>
/// <list type="table">
///   <listheader>
///     <term>Value</term><description>CloudWatch mapping</description>
///   </listheader>
///   <item>
///     <term><c>GaugeValue</c>, <c>CounterValue</c></term>
///     <description><see cref="MetricDatum.Value"/></description>
///   </item>
///   <item>
///     <term><c>BucketHistogramValue</c>, <c>DistributionValue</c>, <c>SummaryValue</c></term>
///     <description><see cref="MetricDatum.StatisticValues"/> (count/min/max/sum)</description>
///   </item>
///   <item>
///     <term><c>MultiSampleValue</c></term>
///     <description>Either flattens to multiple single datapoints or averages into one, depending on options.</description>
///   </item>
/// </list>
/// <para>
/// <b>Batching and retries.</b> Metrics are buffered and pushed via 
/// <see cref="IAmazonCloudWatch.PutMetricDataAsync(PutMetricDataRequest, System.Threading.CancellationToken)"/>
/// in batches (1–20). Retryable errors (throttling/HTTP 5xx) are retried with exponential backoff and jitter.
/// </para>
/// <para>
/// <b>Thread safety.</b> Instances are intended to be used across threads; <see cref="ExportAsync"/> is safe to call concurrently,
/// but call sites should avoid interleaving options mutations while exporting.
/// </para>
/// <para>
/// <b>Trimming/AOT.</b> Some helper methods optionally use reflection to read public properties from metric abstractions.
/// See the <see cref="RequiresUnreferencedCodeAttribute"/> annotation on 
/// <see cref="ExportAsync(System.Collections.Generic.IEnumerable{IMetric}, System.Threading.CancellationToken)"/> for details.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Example: configure and use CloudWatchExporter
/// using Amazon.CloudWatch;
/// using Microsoft.Extensions.DependencyInjection;
/// using Microsoft.Extensions.Options;
/// using NetMetric.Export.CloudWatch.Exporters;
///
/// var services = new ServiceCollection();
///
/// services.AddSingleton<IAmazonCloudWatch>(sp => new AmazonCloudWatchClient());
/// services.Configure<CloudWatchExporterOptions>(opt =>
/// {
///     opt.Namespace = "MyApp/Telemetry";
///     opt.MaxBatchSize = 20;
///     opt.MaxRetries = 3;
///     opt.RetryBaseDelayMs = 100;
///     opt.FlattenMultiSample = false;
///     opt.StorageResolution = 60; // standard resolution
/// });
/// services.AddSingleton<CloudWatchExporter>();
///
/// var provider = services.BuildServiceProvider();
/// var exporter = provider.GetRequiredService<CloudWatchExporter>();
///
/// // Collect some metrics (IMetric instances) and export:
/// IReadOnlyList<IMetric> metrics = CollectMetricsFromYourApp();
/// await exporter.ExportAsync(metrics, CancellationToken.None);
/// ]]></code>
/// </example>
public sealed class CloudWatchExporter : IMetricExporter
{
    private readonly IAmazonCloudWatch _cw;
    private readonly CloudWatchExporterOptions _opt;

    /// <summary>
    /// Initializes a new instance of the <see cref="CloudWatchExporter"/> class.
    /// </summary>
    /// <param name="cloudWatch">The Amazon CloudWatch client used to send metric data.</param>
    /// <param name="options">Optional exporter configuration; when omitted, sensible defaults are used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="cloudWatch"/> is <see langword="null"/>.</exception>
    public CloudWatchExporter(
        IAmazonCloudWatch cloudWatch,
        IOptions<CloudWatchExporterOptions>? options = null)
    {
        _cw = cloudWatch ?? throw new ArgumentNullException(nameof(cloudWatch));
        _opt = options?.Value ?? new CloudWatchExporterOptions();
    }

    /// <summary>
    /// Exports a collection of metrics asynchronously to Amazon CloudWatch.
    /// </summary>
    /// <param name="metrics">The metrics to export.</param>
    /// <param name="ct">A token that can be used to cancel the operation.</param>
    /// <returns>A task that completes when the export attempt finishes (including retries).</returns>
    /// <remarks>
    /// <para>
    /// The method performs the following steps:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>Transforms each input metric into one or more <see cref="MetricDatum"/> instances.</description>
    ///   </item>
    ///   <item>
    ///     <description>Batches datums into groups of up to the configured maximum (1–20).</description>
    ///   </item>
    ///   <item>
    ///     <description>Sends each batch using <see cref="IAmazonCloudWatch.PutMetricDataAsync(PutMetricDataRequest, System.Threading.CancellationToken)"/> with retry on throttling/5xx.</description>
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Multi-sample metrics.</b> When <c>FlattenMultiSample</c> is enabled in options, each item within a multi-sample value
    /// is emitted as its own datum with merged tags. Otherwise, the exporter computes the arithmetic mean and emits a single datum.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="AmazonCloudWatchException">An AWS error occurs while publishing data (after retries have been exhausted, if any).</exception>
    [RequiresUnreferencedCode("IMetricExporter.ExportAsync may use reflection or members trimmed at AOT; keep members or disable trimming for exporters.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var datums = new List<MetricDatum>(128);
        var now = DateTime.UtcNow;

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();

            var value = m.GetValue();
            if (value is null)
            {
                continue;
            }

            var (kind, unitStr) = GetKindAndUnit(m);
            var defaultUnit = UnitFrom(unitStr, StandardUnit.None);
            var statsUnit = UnitFrom(unitStr, GuessUnitFromKind(kind));

            switch (value)
            {
                case GaugeValue g:
                    datums.Add(NewValueDatum(now, m, g.Value, defaultUnit));
                    break;

                case CounterValue c:
                    datums.Add(NewValueDatum(now, m, c.Value, StandardUnit.Count, forceTotalSuffix: true));
                    break;

                case BucketHistogramValue h:
                    datums.Add(NewStatDatum(
                        now, m,
                        sampleCount: Math.Max(1, h.Count),
                        min: h.Min,
                        max: h.Max,
                        sum: h.Sum,
                        unit: statsUnit));
                    break;

                case DistributionValue d:
                    datums.Add(NewStatDatum(
                        now, m,
                        sampleCount: Math.Max(1, d.Count),
                        min: d.Min,
                        max: d.Max,
                        sum: ApproxSum(d.Count, (d.P50 + d.P90 + d.P99) / 3.0),
                        unit: statsUnit));
                    break;

                case SummaryValue s:
                    {
                        var p50 = EstimateMedian(s);
                        datums.Add(NewStatDatum(
                            now, m,
                            sampleCount: Math.Max(1, s.Count),
                            min: s.Min,
                            max: s.Max,
                            sum: ApproxSum(s.Count, p50),
                            unit: statsUnit));
                        break;
                    }

                case MultiSampleValue ms:
                    if (_opt.FlattenMultiSample)
                    {
                        foreach (var item in ms.Items)
                        {
                            var dv = TryDouble(item.Value);
                            if (dv is null) continue;

                            var tags = MergeTags(m.Tags, item.Tags);

                            datums.Add(new MetricDatum
                            {
                                MetricName = Trunc(Clean(string.IsNullOrWhiteSpace(item.Name) ? (m.Name ?? m.Id) : item.Name), 255),
                                Timestamp = now,
                                Unit = defaultUnit,
                                Value = dv.Value,
                                Dimensions = new List<Dimension>(Dimensions(tags)),
                                StorageResolution = _opt.StorageResolution
                            });
                        }
                    }
                    else
                    {
                        var avg = AverageOfMultiSample(ms);

                        datums.Add(NewValueDatum(now, m, avg, defaultUnit));
                    }
                    break;

                default:

                    datums.Add(NewValueDatum(now, m, 0d, StandardUnit.None));

                    break;
            }
        }

        int batchSize = Math.Clamp(_opt.MaxBatchSize, 1, 20);
        for (int i = 0; i < datums.Count; i += batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = datums.GetRange(i, Math.Min(batchSize, datums.Count - i));
            var req = new PutMetricDataRequest { Namespace = _opt.Namespace, MetricData = batch };

            try
            {
                await PutWithRetryAsync(req, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AmazonCloudWatchException)
            {
                throw;
            }
        }
    }

    // ---------- Helpers ----------

    /// <summary>
    /// Estimates the median (p50) of a <see cref="SummaryValue"/>.
    /// </summary>
    /// <param name="s">The summary value to read quantiles from.</param>
    /// <returns>The median (p50) when present; otherwise the average of <see cref="SummaryValue.Min"/> and <see cref="SummaryValue.Max"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    public static double EstimateMedian(SummaryValue s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s.Quantiles.TryGetValue(0.5, out var mid) ? mid : (s.Min + s.Max) / 2.0;
    }

    /// <summary>
    /// Computes the arithmetic mean of numeric items within a <see cref="MultiSampleValue"/>.
    /// </summary>
    /// <param name="ms">The multi-sample container.</param>
    /// <returns>
    /// The average over items that are <c>GaugeValue</c> or <c>CounterValue</c>; returns <c>0</c> when there are no numeric items.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="ms"/> is <see langword="null"/>.</exception>
    public static double AverageOfMultiSample(MultiSampleValue ms)
    {
        ArgumentNullException.ThrowIfNull(ms);

        if (ms.Items.Count == 0)
        {
            return 0d;
        }

        double sum = 0d;
        int cnt = 0;

        foreach (var item in ms.Items)
        {
            var dv = TryDouble(item.Value);

            if (dv.HasValue)
            {
                sum += dv.Value;
                cnt++;
            }
        }

        return cnt == 0 ? 0d : (sum / cnt);
    }

    /// <summary>
    /// Executes <see cref="IAmazonCloudWatch.PutMetricDataAsync(PutMetricDataRequest, System.Threading.CancellationToken)"/>
    /// with retry on retryable conditions (throttling and HTTP 5xx).
    /// </summary>
    /// <param name="req">The request containing the batch of datums to publish.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the request succeeds or a non-retryable error occurs.</returns>
    /// <exception cref="OperationCanceledException">The operation is canceled via <paramref name="ct"/>.</exception>
    /// <exception cref="AmazonCloudWatchException">A non-retryable error occurs, or retries are exhausted.</exception>
    private async Task PutWithRetryAsync(PutMetricDataRequest req, CancellationToken ct)
    {
        int attempt = 0;
        while (true)
        {
            try
            {
                await _cw.PutMetricDataAsync(req, ct).ConfigureAwait(false);

                return;
            }
            catch (AmazonCloudWatchException ex) when (IsRetryable(ex) && attempt++ < _opt.MaxRetries)
            {
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Indicates whether a CloudWatch exception is considered retryable.
    /// </summary>
    /// <param name="ex">The exception returned by the AWS SDK.</param>
    /// <returns><see langword="true"/> if the error represents throttling or an HTTP 5xx; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ex"/> is <see langword="null"/>.</exception>
    public static bool IsRetryable(AmazonCloudWatchException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || string.Equals(ex.ErrorCode, "Throttling", StringComparison.OrdinalIgnoreCase)
            || (int)ex.StatusCode >= 500;
    }

    /// <summary>
    /// Computes an exponential backoff delay with decorrelated jitter.
    /// </summary>
    /// <param name="attempt">The zero-based attempt number.</param>
    /// <returns>A randomized delay for the next retry.</returns>
    private TimeSpan Backoff(int attempt)
    {
        var baseMs = Math.Max(50, _opt.RetryBaseDelayMs);
        var exp = baseMs * (int)Math.Pow(2, Math.Clamp(attempt, 0, 6));

        int jitter;
        using (var rng = RandomNumberGenerator.Create())
        {
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            jitter = BitConverter.ToInt32(bytes, 0) & int.MaxValue;
            jitter %= baseMs;
        }

        return TimeSpan.FromMilliseconds(exp + jitter);
    }

    /// <summary>
    /// Creates a single-value <see cref="MetricDatum"/>.
    /// </summary>
    /// <param name="now">The UTC timestamp to assign to the datum.</param>
    /// <param name="m">The source metric.</param>
    /// <param name="value">The numeric value to publish.</param>
    /// <param name="unit">The CloudWatch unit to apply.</param>
    /// <param name="forceTotalSuffix">When <see langword="true"/>, ensures a <c>_total</c> suffix is present on the metric name (for counters).</param>
    /// <returns>The constructed datum ready for publishing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    public MetricDatum NewValueDatum(DateTime now, IMetric m, double value, StandardUnit unit, bool forceTotalSuffix = false)
    {
        ArgumentNullException.ThrowIfNull(m);

        var baseName = forceTotalSuffix ? EnsureTotalSuffix(m.Name ?? m.Id) : (m.Name ?? m.Id);

        return new MetricDatum
        {
            MetricName = Trunc(Clean(baseName), 255),
            Timestamp = now,
            Unit = unit,
            Value = value,
            Dimensions = new List<Dimension>(Dimensions(m.Tags)),
            StorageResolution = _opt.StorageResolution
        };
    }

    /// <summary>
    /// Creates a statistics <see cref="MetricDatum"/> (count/min/max/sum).
    /// </summary>
    /// <param name="now">The UTC timestamp to assign to the datum.</param>
    /// <param name="m">The source metric.</param>
    /// <param name="sampleCount">The number of samples aggregated into the statistics.</param>
    /// <param name="min">The minimum sample value.</param>
    /// <param name="max">The maximum sample value.</param>
    /// <param name="sum">The arithmetic sum of all samples.</param>
    /// <param name="unit">The CloudWatch unit to apply.</param>
    /// <returns>The constructed datum with <see cref="StatisticSet"/> populated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    public MetricDatum NewStatDatum(DateTime now, IMetric m, long sampleCount, double min, double max, double sum, StandardUnit unit)
    {
        ArgumentNullException.ThrowIfNull(m);

        return new MetricDatum
        {
            MetricName = Trunc(Clean(m.Name ?? m.Id), 255),
            Timestamp = now,
            Unit = unit,
            StatisticValues = new StatisticSet
            {
                SampleCount = Math.Max(1, sampleCount),
                Minimum = min,
                Maximum = max,
                Sum = sum
            },
            Dimensions = new List<Dimension>(Dimensions(m.Tags)),
            StorageResolution = _opt.StorageResolution
        };
    }

    // ---- Mapping helpers ----

    /// <summary>
    /// Approximates a sum when only a representative value is available.
    /// </summary>
    /// <param name="count">The sample count.</param>
    /// <param name="representative">The representative value (e.g., median).</param>
    /// <returns>Either <paramref name="representative"/>×<paramref name="count"/> or <paramref name="representative"/>, depending on options.</returns>
    private double ApproxSum(long count, double representative)
    {
        return _opt.ApproximateSumWhenMissing ? representative * Math.Max(1, count) : representative;
    }

    /// <summary>
    /// Attempts to extract a numeric value from a metric value.
    /// </summary>
    /// <param name="v">The metric value.</param>
    /// <returns>The numeric value when recognized; otherwise, <see langword="null"/>.</returns>
    private static double? TryDouble(MetricValue v) => v switch
    {
        GaugeValue g => g.Value,
        CounterValue c => c.Value, // implicit long->double
        _ => null
    };

    /// <summary>
    /// Ensures a metric name ends with the conventional counter suffix <c>_total</c>.
    /// </summary>
    /// <param name="name">The base metric name.</param>
    /// <returns>The original name or the name with <c>_total</c> appended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public static string EnsureTotalSuffix(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.EndsWith("_total", StringComparison.Ordinal)
            ? name
            : string.Concat(name, "_total");
    }

    /// <summary>
    /// Infers a sensible default unit from the instrument kind when an explicit unit is not provided.
    /// </summary>
    /// <param name="kind">The instrument kind.</param>
    /// <returns>A <see cref="StandardUnit"/> to use as default.</returns>
    private static StandardUnit GuessUnitFromKind(InstrumentKind kind) => kind switch
    {
        InstrumentKind.Counter => StandardUnit.Count,
        InstrumentKind.Histogram => StandardUnit.None,
        InstrumentKind.Summary => StandardUnit.None,
        InstrumentKind.MultiSample => StandardUnit.None,
        InstrumentKind.Gauge => StandardUnit.None,
        _ => StandardUnit.None
    };

    /// <summary>
    /// Maps a textual unit to a <see cref="StandardUnit"/>, falling back to the provided default.
    /// </summary>
    /// <param name="unitStr">The unit string (case-insensitive), e.g., <c>"ms"</c>, <c>"bytes"</c>, <c>"percent"</c>.</param>
    /// <param name="fallback">The default unit when mapping fails.</param>
    /// <returns>The mapped unit or <paramref name="fallback"/>.</returns>
    private static StandardUnit UnitFrom(string? unitStr, StandardUnit fallback)
    {
        if (string.IsNullOrWhiteSpace(unitStr)) return fallback;

        // Compare in uppercase for robustness
        switch (unitStr!.ToUpperInvariant())
        {
            case "MS":
            case "MILLISECOND":
            case "MILLISECONDS":
                return StandardUnit.Milliseconds;

            case "S":
            case "SEC":
            case "SECOND":
            case "SECONDS":
                return StandardUnit.Seconds;

            case "BYTE":
            case "BYTES":
                return StandardUnit.Bytes;

            case "PERCENT":
            case "%":
                return StandardUnit.Percent;

            case "COUNT":
                return StandardUnit.Count;

            default:
                return fallback;
        }
    }

    /// <summary>
    /// Converts a tag dictionary into an ordered, size-limited, read-only collection of CloudWatch <see cref="Dimension"/> values.
    /// </summary>
    /// <param name="tags">The tag key/value pairs; may be <see langword="null"/> or empty.</param>
    /// <returns>An immutable list of dimensions.</returns>
    private ReadOnlyCollection<Dimension> Dimensions(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return new ReadOnlyCollection<Dimension>(Array.Empty<Dimension>());
        }

        var list = tags
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(_opt.MaxDimensions)
            .Select(kv => new Dimension
            {
                Name = Trunc(Clean(kv.Key), 255),
                Value = Trunc(Clean(kv.Value), 255)
            })
            .ToList();

        return new ReadOnlyCollection<Dimension>(list);
    }

    /// <summary>
    /// Merges two tag sets, with the right-hand set overriding on key conflicts.
    /// </summary>
    /// <param name="a">The left (base) tag set.</param>
    /// <param name="b">The right (override) tag set.</param>
    /// <returns>A new dictionary containing the merged tags.</returns>
    private static IReadOnlyDictionary<string, string> MergeTags(
        IReadOnlyDictionary<string, string>? a,
        IReadOnlyDictionary<string, string>? b)
    {
        if (a is null || a.Count == 0)
        {
            return b ?? new Dictionary<string, string>(0, StringComparer.Ordinal);
        }
        if (b is null || b.Count == 0)
        {
            return a;
        }

        var d = new Dictionary<string, string>(a, StringComparer.Ordinal);

        foreach (var kv in b)
        {
            d[kv.Key] = kv.Value ?? string.Empty;
        }

        return d;
    }

    /// <summary>
    /// Discovers the instrument kind and textual unit of the provided metric, using reflection for compatibility across implementations.
    /// </summary>
    /// <typeparam name="T">A metric type implementing <see cref="IMetric"/>.</typeparam>
    /// <param name="m">The metric instance.</param>
    /// <returns>A tuple containing the discovered <see cref="InstrumentKind"/> and the unit string (if any).</returns>
    /// <remarks>
    /// The method first attempts to read public instance properties named <c>Unit</c> and <c>Kind</c>. If they are missing,
    /// the kind is inferred from the shape of the value returned by <c>IMetric.GetValue()</c>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2070",
        Justification = "Accessing public properties 'Unit' and 'Kind' by name for cross-type compatibility. Names are constants.")]
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026",
        Justification = "Reflection is limited to getting public instance properties 'Unit' and 'Kind'.")]
    public static (InstrumentKind Kind, string? Unit) GetKindAndUnit<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T m)
    where T : IMetric
    {
        ArgumentNullException.ThrowIfNull(m);

        var t = typeof(T);

        string? unit = t.GetProperty("Unit", BindingFlags.Public | BindingFlags.Instance)?.GetValue(m) as string;

        InstrumentKind? kind = null;

        var kindProp = t.GetProperty("Kind", BindingFlags.Public | BindingFlags.Instance);

        if (kindProp is not null)
        {
            var kv = kindProp.GetValue(m);

            if (kv is InstrumentKind k)
            {
                kind = k;
            }
            else if (kv is int ki)
            {
                kind = (InstrumentKind)ki;
            }
        }

        kind ??= m.GetValue() switch
        {
            GaugeValue => InstrumentKind.Gauge,
            CounterValue => InstrumentKind.Counter,
            BucketHistogramValue or DistributionValue => InstrumentKind.Histogram,
            SummaryValue => InstrumentKind.Summary,
            MultiSampleValue => InstrumentKind.MultiSample,
            _ => InstrumentKind.Gauge
        };

        return (kind ?? InstrumentKind.Gauge, unit);
    }

    /// <summary>
    /// Replaces newlines, trims whitespace, and coalesces <see langword="null"/> to an empty string.
    /// </summary>
    /// <param name="s">The input string.</param>
    /// <returns>A single-line, trimmed string.</returns>
    private static string Clean(string? s)
    {
        return (s ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
    }

    /// <summary>
    /// Truncates a string to a maximum length.
    /// </summary>
    /// <param name="s">The input string.</param>
    /// <param name="max">The maximum number of characters to keep.</param>
    /// <returns><paramref name="s"/> when its length is within the limit; otherwise the truncated prefix.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    public static string Trunc(string s, int max)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s.Length <= max ? s : s[..max];
    }
}
