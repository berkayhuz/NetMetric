// <copyright file="PrometheusFormatter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace NetMetric.Export.Prometheus.Formatting;

/// <summary>
/// Formats NetMetric metrics into the Prometheus <c>0.0.4</c> text exposition format.
/// </summary>
/// <remarks>
/// <para>
/// This formatter performs a visitor-style dispatch over <see cref="MetricValue"/> subtypes
/// to render counters, gauges, summaries, histograms, distributions, and composite
/// multi-sample metrics into Prometheus-compatible lines.
/// </para>
/// <para>
/// It is typically used by <see cref="PrometheusTextExporter"/> to emit output consumable
/// by Prometheus scrapers at the standard <c>/metrics</c> endpoint.
/// </para>
/// <para>
/// The formatter writes to the provided <see cref="TextWriter"/>. Calls are not thread-safe;
/// concurrent invocations must synchronize access to the underlying writer.
/// </para>
/// <para>
/// <b>Trimming:</b> Some operations rely on reflection (e.g., reading <c>Value</c> or
/// calling <c>GetValue</c>/<c>ReadValue</c>/<c>Snapshot</c> on metrics). When publishing
/// with trimming enabled, ensure required members are preserved via a linker descriptor
/// or attributes such as <see cref="DynamicDependencyAttribute"/>.
/// </para>
/// <para>
/// <b>Prometheus specifics:</b> Counter names are normalized to the canonical <c>_total</c>
/// suffix. Histograms write <c>_bucket</c> samples with <c>le</c> labels (including an
/// explicit <c>+Inf</c> bucket), plus <c>_sum</c> and <c>_count</c>. Summaries and
/// distributions emit quantiles using a <c>quantile</c> label. Optional metadata
/// (<c># HELP</c>, <c># TYPE</c>) and timestamps can be included via
/// <see cref="PrometheusExporterOptions"/>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using System.IO;
/// using NetMetric.Export.Prometheus.Formatting;
///
/// var metrics = GetMetrics(); // IEnumerable<IMetric>
/// using var writer = new StringWriter();
/// var formatter = new PrometheusFormatter(writer, new PrometheusExporterOptions
/// {
///     IncludeMetaLines = true,
///     IncludeTimestamps = false,
///     QuantileLabelPrecision = 3
/// });
/// await formatter.WriteAsync(metrics);
///
/// // writer.ToString() now contains Prometheus 0.0.4 text
/// ]]></code>
/// </example>
/// <seealso cref="PrometheusTextExporter"/>
/// <seealso cref="PrometheusExporterOptions"/>
public sealed class PrometheusFormatter
{
    private readonly TextWriter _writer;
    private readonly PrometheusExporterOptions _opt;
    private readonly CultureInfo _ci = CultureInfo.InvariantCulture;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrometheusFormatter"/> class.
    /// </summary>
    /// <param name="writer">The target <see cref="TextWriter"/> that receives formatted output.</param>
    /// <param name="options">
    /// Optional exporter configuration; when <see langword="null"/>, a new
    /// <see cref="PrometheusExporterOptions"/> instance is used.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="writer"/> is <see langword="null"/>.</exception>
    public PrometheusFormatter(TextWriter writer, PrometheusExporterOptions? options = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _opt = options ?? new PrometheusExporterOptions();
    }

    /// <summary>
    /// Writes the provided metrics to the underlying <see cref="TextWriter"/> using the Prometheus format.
    /// </summary>
    /// <param name="metrics">A sequence of metrics to serialize.</param>
    /// <param name="ct">A token used to observe cancellation.</param>
    /// <returns>A completed <see cref="Task"/> when all metrics have been written.</returns>
    /// <remarks>
    /// The method completes synchronously and returns a completed task. Each metric is
    /// visited and rendered in sequence. If a metric cannot produce a <see cref="MetricValue"/>,
    /// it is skipped.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="metrics"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(
        "PrometheusFormatter uses reflection. When trimming is enabled, public properties and methods of metric " +
        "implementations must be preserved (via a linker descriptor or DynamicDependency).")]
    public Task WriteAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        foreach (var m in metrics)
        {
            ct.ThrowIfCancellationRequested();
            if (m is null) continue;

            var val = TryReadValue(m);
            if (val is null) continue;

            _cachedDescription = TryGetDescription(m);
            Dispatch(val, m);
        }

        return Task.CompletedTask;
    }

    // ----------------- Dispatch -----------------

    /// <summary>
    /// Routes the given <see cref="MetricValue"/> to the appropriate visitor overload for formatting.
    /// </summary>
    /// <param name="v">The metric value to render.</param>
    /// <param name="m">The metric instance that produced the value.</param>
    private void Dispatch(MetricValue v, IMetric m)
    {
        switch (v)
        {
            case CounterValue x: Visit(x, m); break;
            case GaugeValue x: Visit(x, m); break;
            case DistributionValue x: Visit(x, m); break;
            case SummaryValue x: Visit(x, m); break;
            case BucketHistogramValue x: Visit(x, m); break;
            case MultiSampleValue x: Visit(x, m); break;
            default: Visit(v, m); break;
        }
    }

    /// <summary>
    /// Maps a <see cref="MetricValue"/> subtype to its Prometheus <c># TYPE</c> token.
    /// </summary>
    /// <param name="v">The metric value.</param>
    /// <returns>
    /// One of <c>counter</c>, <c>gauge</c>, <c>summary</c>, or <c>histogram</c>.
    /// Unknown types default to <c>gauge</c>.
    /// </returns>
    private static string PromTypeFor(MetricValue v) => v switch
    {
        CounterValue => "counter",
        GaugeValue => "gauge",
        DistributionValue => "summary",
        SummaryValue => "summary",
        BucketHistogramValue => "histogram",
        MultiSampleValue => "gauge",
        _ => "gauge"
    };

    /// <summary>
    /// Computes the sanitized Prometheus metric name for the supplied metric.
    /// </summary>
    /// <param name="m">The metric.</param>
    /// <returns>An ASCII-safe, Prometheus-compliant metric name.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="m"/> is <see langword="null"/>.</exception>
    private string Name(IMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);

        return PrometheusName.SanitizeMetricName(m.Name ?? m.Id ?? "netmetric_unnamed", _opt.AsciiOnlyMetricNames);
    }

    /// <summary>
    /// Ensures the provided name uses the canonical <c>_total</c> suffix for counters.
    /// </summary>
    /// <param name="name">The base metric name.</param>
    /// <returns>The name with <c>_total</c> appended, if missing.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="name"/> is <see langword="null"/>.</exception>
    private static string EnsureCounterTotalSuffix(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.EndsWith("_total", StringComparison.Ordinal) ? name : name + "_total";
    }

    /// <summary>
    /// Returns the current Unix epoch time in milliseconds (UTC).
    /// </summary>
    private static long EpochMsUtc()
    {
        return (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
    }

    private string? _cachedDescription;

    /// <summary>
    /// Writes <c># HELP</c> and <c># TYPE</c> metadata lines for the metric, if enabled by options.
    /// </summary>
    /// <param name="m">The metric.</param>
    /// <param name="v">The value determining the Prometheus type.</param>
    private void WriteMeta(IMetric m, MetricValue v)
    {
        if (!_opt.IncludeMetaLines)
        {
            return;
        }

        var baseName = Name(m);

        if (_opt.DeriveHelpFromDescription && !string.IsNullOrEmpty(_cachedDescription))
        {
            _writer.WriteLine($"# HELP {baseName} {PrometheusName.EscapeHelp(_cachedDescription!)}");
        }

        _writer.WriteLine($"# TYPE {GetMetaName(m, v)} {PromTypeFor(v)}");
    }

    /// <summary>
    /// Returns the metric name to use in metadata lines, applying counter suffix rules as needed.
    /// </summary>
    /// <param name="m">The metric.</param>
    /// <param name="v">The associated value.</param>
    /// <returns>The name for use in <c># TYPE</c> (and implicitly <c># HELP</c>).</returns>
    private string GetMetaName(IMetric m, MetricValue v)
    {
        var n = Name(m);
        return v is CounterValue ? EnsureCounterTotalSuffix(n) : n;
    }

    /// <summary>
    /// Writes a single Prometheus sample line with optional labels.
    /// </summary>
    /// <param name="name">The metric name (already sanitized if necessary).</param>
    /// <param name="m">The owning metric instance.</param>
    /// <param name="extraLabels">Labels to append to the sample; may be <see langword="null"/>.</param>
    /// <param name="value">The numeric sample value.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="name"/> or <paramref name="m"/> is <see langword="null"/>.
    /// </exception>
    private void WriteSample(string name, IMetric m, IReadOnlyDictionary<string, string>? extraLabels, double value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(m);

        var labels = extraLabels ?? ReadOnlyDictionary<string, string>.Empty;

        _writer.Write(name);

        if (labels.Count > 0)
        {
            _writer.Write('{');
            bool first = true;
            foreach (var kv in labels.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (!first) _writer.Write(',');
                first = false;

                var key = PrometheusName.SanitizeMetricName(kv.Key, asciiOnly: true);
                var val = PrometheusName.EscapeLabelValue(kv.Value ?? string.Empty, _opt.LabelMaxLength);

                _writer.Write(key);
                _writer.Write("=\"");
                _writer.Write(val);
                _writer.Write('"');
            }
            _writer.Write('}');
        }

        _writer.Write(' ');
        _writer.Write(value.ToString("0.################", _ci));

        if (_opt.IncludeTimestamps)
        {
            _writer.Write(' ');
            _writer.Write(EpochMsUtc().ToString(_ci));
        }

        _writer.WriteLine();
    }

    /// <summary>
    /// Merges two label dictionaries, with entries from <paramref name="b"/> overwriting
    /// any conflicting keys from <paramref name="a"/>.
    /// </summary>
    /// <param name="a">The base label set (required).</param>
    /// <param name="b">The optional label set to overlay.</param>
    /// <returns>An immutable view of the merged labels.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="a"/> is <see langword="null"/>.</exception>
    private static IReadOnlyDictionary<string, string> MergeLabels(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string>? b)
    {
        ArgumentNullException.ThrowIfNull(a);

        if (b is null || b.Count == 0)
        {
            return a;
        }

        if (a.Count == 0)
        {
            return b;
        }

        var dict = new Dictionary<string, string>(a.Count + b.Count, StringComparer.Ordinal);

        foreach (var kv in a)
        {
            dict[kv.Key] = kv.Value;
        }
        foreach (var kv in b)
        {
            dict[kv.Key] = kv.Value;
        }

        return dict;
    }

    // ----------------- Visitors -----------------

    /// <summary>
    /// Formats a single gauge sample.
    /// </summary>
    /// <param name="v">The gauge value.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(GaugeValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = Name(m);

        WriteSample(baseName, m, null, v.Value);
    }

    /// <summary>
    /// Formats a single counter sample, ensuring the canonical <c>_total</c> suffix.
    /// </summary>
    /// <param name="v">The counter value.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(CounterValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = EnsureCounterTotalSuffix(Name(m));

        WriteSample(baseName, m, null, v.Value);
    }

    /// <summary>
    /// Formats a distribution metric by emitting count/min/max and standard quantiles.
    /// </summary>
    /// <param name="v">The distribution value.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(DistributionValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = Name(m);

        WriteSample(baseName + "_count", m, null, v.Count);
        WriteSample(baseName + "_min", m, null, v.Min);
        WriteSample(baseName + "_max", m, null, v.Max);

        var q = new (double q, double val)[] { (0.5, v.P50), (0.9, v.P90), (0.99, v.P99) };
        foreach (var (quantile, val) in q)
        {
            var extra = new Dictionary<string, string>
            {
                ["quantile"] = quantile.ToString($"0.{new string('#', _opt.QuantileLabelPrecision)}", _ci)
            };
            WriteSample(baseName, m, extra, val);
        }
    }

    /// <summary>
    /// Formats a summary metric with quantile samples, plus min and max.
    /// </summary>
    /// <param name="v">The summary value.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(SummaryValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = Name(m);

        WriteSample(baseName + "_count", m, null, v.Count);
        foreach (var kv in v.Quantiles.OrderBy(x => x.Key))
        {
            var extra = new Dictionary<string, string>
            {
                ["quantile"] = kv.Key.ToString($"0.{new string('#', _opt.QuantileLabelPrecision)}", _ci)
            };
            WriteSample(baseName, m, extra, kv.Value);
        }

        WriteSample(baseName + "_min", m, null, v.Min);
        WriteSample(baseName + "_max", m, null, v.Max);
    }

    /// <summary>
    /// Formats a histogram metric by emitting bucket counts, sum, count, and min/max.
    /// </summary>
    /// <param name="v">The histogram value.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(BucketHistogramValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = Name(m);

        for (int i = 0; i < v.Buckets.Count; i++)
        {
            var labels = new Dictionary<string, string> { ["le"] = v.Buckets[i].ToString("0.############", _ci) };
            WriteSample(baseName + "_bucket", m, labels, v.Counts[i]);
        }

        // +Inf
        {
            var labels = new Dictionary<string, string> { ["le"] = "+Inf" };
            WriteSample(baseName + "_bucket", m, labels, v.Counts[^1]);
        }

        WriteSample(baseName + "_sum", m, null, v.Sum);
        WriteSample(baseName + "_count", m, null, v.Count);

        WriteSample(baseName + "_min", m, null, v.Min);
        WriteSample(baseName + "_max", m, null, v.Max);
    }

    /// <summary>
    /// Formats a composite multi-sample metric by writing per-item samples.
    /// </summary>
    /// <param name="v">The multi-sample value containing item entries.</param>
    /// <param name="m">The owning metric instance.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="v"/> is <see langword="null"/>.</exception>
    public void Visit(MultiSampleValue v, IMetric m)
    {
        ArgumentNullException.ThrowIfNull(v);

        WriteMeta(m, v);

        var baseName = Name(m);

        WriteSample(baseName + "_items", m, null, v.Items.Count);

        foreach (var item in v.Items)
        {
            var extra = new Dictionary<string, string>
            {
                ["id"] = item.Id ?? string.Empty,
                ["name"] = item.Name ?? string.Empty
            };

            if (item.Tags is { Count: > 0 })
            {
                foreach (var kv in item.Tags)
                    extra[kv.Key] = kv.Value;
            }

            TryWriteMetricValue(baseName, m, extra, item.Value);
        }
    }

    /// <summary>
    /// Fallback visitor for unknown values; writes a zero-valued gauge sample.
    /// </summary>
    /// <param name="v">The unrecognized metric value.</param>
    /// <param name="m">The owning metric instance.</param>
    public void Visit(MetricValue v, IMetric m)
    {
        // Unknown value types → gauge:0
        WriteMeta(m, v);
        WriteSample(Name(m), m, null, 0);
    }

    /// <summary>
    /// Uses reflection to retrieve a public instance property by name.
    /// </summary>
    /// <param name="t">The declaring <see cref="Type"/>.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The <see cref="PropertyInfo"/> if found; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="t"/> or <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    private static PropertyInfo? GetPublicProperty(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type t,
        string name)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(name);

        return t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
    }

    /// <summary>
    /// Uses reflection to retrieve a public instance method by name and signature.
    /// </summary>
    /// <param name="t">The declaring <see cref="Type"/>.</param>
    /// <param name="name">The method name.</param>
    /// <param name="parameters">Optional parameter types; <see langword="null"/> or empty for parameterless.</param>
    /// <returns>The <see cref="MethodInfo"/> if found; otherwise <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="t"/> or <paramref name="name"/> is <see langword="null"/>.
    /// </exception>
    private static MethodInfo? GetPublicMethod(Type t, string name, Type[]? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(name);

        [UnconditionalSuppressMessage("Trimming", "IL2070",
            Justification = "Members are preserved by linker configuration in publish-trim scenarios.")]
        static MethodInfo? GetMethCore(Type tt, string nn, Type[]? pars)
            => tt.GetMethod(nn,
                            BindingFlags.Instance | BindingFlags.Public,
                            binder: null,
                            types: pars ?? Type.EmptyTypes,
                            modifiers: null);
        return GetMethCore(t, name, parameters);
    }

    /// <summary>
    /// Attempts to read a <see cref="MetricValue"/> from the supplied metric by probing
    /// common properties or methods via reflection.
    /// </summary>
    /// <param name="m">The metric to inspect.</param>
    /// <returns>
    /// A concrete <see cref="MetricValue"/> if a supported member is found; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// The following members are checked in order:
    /// <list type="number">
    /// <item><description>Public instance property <c>Value</c> of type <see cref="MetricValue"/>.</description></item>
    /// <item><description>Parameterless methods <c>GetValue</c>, <c>ReadValue</c>, or <c>Snapshot</c> returning <see cref="MetricValue"/>.</description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="m"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(
        "Reflection is used on IMetric. Members such as 'Value', 'GetValue', 'ReadValue', or 'Snapshot' are accessed via reflection.")]
    private static MetricValue? TryReadValue(IMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var t = m.GetType();

        var p = GetPublicProperty(t, "Value");
        if (p is not null && typeof(MetricValue).IsAssignableFrom(p.PropertyType))
            return (MetricValue?)p.GetValue(m);

        foreach (var name in new[] { "GetValue", "ReadValue", "Snapshot" })
        {
            var mi = GetPublicMethod(t, name, Type.EmptyTypes);
            if (mi is not null && typeof(MetricValue).IsAssignableFrom(mi.ReturnType))
                return (MetricValue?)mi.Invoke(m, null);
        }
        return null;
    }

    /// <summary>
    /// Attempts to obtain a human-readable description for the metric if exposed by the type.
    /// </summary>
    /// <param name="m">The metric instance.</param>
    /// <returns>
    /// A description string if found (via <c>Description</c> or <c>Metadata.Description</c>); otherwise <see langword="null"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="m"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(
        "Reflection is used on IMetric. The members 'Description' or 'Metadata.Description' are accessed via reflection.")]
    private static string? TryGetDescription(IMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var t = m.GetType();

        var pd = GetPublicProperty(t, "Description");
        if (pd is not null && pd.PropertyType == typeof(string))
            return (string?)pd.GetValue(m);

        var pm = GetPublicProperty(t, "Metadata");
        if (pm is not null)
        {
            var metaType = pm.PropertyType;
            var desc = GetPublicProperty(metaType, "Description");
            if (desc is not null)
            {
                var meta = pm.GetValue(m);
                return (string?)desc.GetValue(meta);
            }
        }
        return null;
    }

    /// <summary>
    /// Formats a nested <see cref="MetricValue"/> in the context of a multi-sample item,
    /// reusing the parent's base name and augmenting labels.
    /// </summary>
    /// <param name="baseName">The base metric name (without counter suffix).</param>
    /// <param name="parent">The parent metric.</param>
    /// <param name="extraLabels">Labels describing the item (e.g., <c>id</c>, <c>name</c>).</param>
    /// <param name="v">The nested value to serialize.</param>
    private void TryWriteMetricValue(string baseName, IMetric parent, IReadOnlyDictionary<string, string> extraLabels, MetricValue v)
    {
        switch (v)
        {
            case GaugeValue g:
                WriteSample(baseName, parent, extraLabels, g.Value);
                break;

            case CounterValue c:
                WriteSample(EnsureCounterTotalSuffix(baseName), parent, extraLabels, c.Value);
                break;

            case DistributionValue d:
                WriteSample(baseName + "_count", parent, extraLabels, d.Count);
                WriteSample(baseName + "_min", parent, extraLabels, d.Min);
                WriteSample(baseName + "_max", parent, extraLabels, d.Max);

                var qd = new (double q, double val)[] { (0.5, d.P50), (0.9, d.P90), (0.99, d.P99) };
                foreach (var (quantile, val) in qd)
                {
                    var labels = new Dictionary<string, string>(extraLabels)
                    {
                        ["quantile"] = quantile.ToString($"0.{new string('#', _opt.QuantileLabelPrecision)}", _ci)
                    };
                    WriteSample(baseName, parent, labels, val);
                }
                break;

            case SummaryValue s:
                WriteSample(baseName + "_count", parent, extraLabels, s.Count);

                foreach (var kv in s.Quantiles.OrderBy(x => x.Key))
                {
                    var labels = new Dictionary<string, string>(extraLabels)
                    {
                        ["quantile"] = kv.Key.ToString($"0.{new string('#', _opt.QuantileLabelPrecision)}", _ci)
                    };
                    WriteSample(baseName, parent, labels, kv.Value);
                }

                WriteSample(baseName + "_min", parent, extraLabels, s.Min);
                WriteSample(baseName + "_max", parent, extraLabels, s.Max);
                break;

            case BucketHistogramValue h:
                for (int i = 0; i < h.Buckets.Count; i++)
                {
                    var labels = new Dictionary<string, string>(extraLabels)
                    {
                        ["le"] = h.Buckets[i].ToString("0.############", _ci)
                    };
                    WriteSample(baseName + "_bucket", parent, labels, h.Counts[i]);
                }

                var infLabels = new Dictionary<string, string>(extraLabels) { ["le"] = "+Inf" };
                WriteSample(baseName + "_bucket", parent, infLabels, h.Counts[^1]);

                WriteSample(baseName + "_sum", parent, extraLabels, h.Sum);
                WriteSample(baseName + "_count", parent, extraLabels, h.Count);
                WriteSample(baseName + "_min", parent, extraLabels, h.Min);
                WriteSample(baseName + "_max", parent, extraLabels, h.Max);
                break;

            default:
                WriteSample(baseName, parent, extraLabels, 0);
                break;
        }
    }
}
