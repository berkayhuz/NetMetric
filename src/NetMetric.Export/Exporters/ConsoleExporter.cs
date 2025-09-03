// <copyright file="ConsoleExporter.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Globalization;

namespace NetMetric.Export.Exporters;

/// <summary>
/// A minimal exporter that writes metrics directly to the console output.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConsoleExporter"/> is the default, zero-dependency exporter intended primarily for
/// development, diagnostics, and very small deployments where a console sink is sufficient.
/// For production scenarios consider using richer exporters (e.g., JSON, file, HTTP, Prometheus)
/// provided by other NetMetric packages.
/// </para>
/// <para>
/// The exporter formats each metric on a single line, including a stable ISO-8601 UTC timestamp,
/// the metric name and identifier, a human-readable value, and optional tags. Newlines in tag keys
/// or values are sanitized to preserve one-line rendering.
/// </para>
/// <para>
/// Thread-safety: instances are safe to use concurrently from multiple threads, provided that the
/// supplied <see cref="IConsoleWriter"/> implementation is thread-safe (the default
/// <see cref="ConsoleWriter"/> is).
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using NetMetric.Abstractions;
/// using NetMetric.Export.Exporters;
///
/// var metrics = new IMetric[]
/// {
///     Metric.Gauge("app.version", "1"),
///     Metric.Counter("requests.total", 42, new Dictionary<string,string> { ["method"] = "GET" })
/// };
///
/// // Write to Console.Out with default UTC clock
/// var exporter = new ConsoleExporter();
/// await exporter.ExportAsync(metrics, CancellationToken.None);
/// ]]></code>
/// </example>
public sealed class ConsoleExporter : IMetricExporter
{
    private readonly ITimeProvider _clock;
    private readonly IConsoleWriter _consoleWriter;

    /// <summary>
    /// Replaces newline characters with spaces to keep tag payloads on a single line.
    /// </summary>
    /// <param name="s">The input string to sanitize. Must not be <see langword="null"/>.</param>
    /// <returns>The sanitized string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    private static string Sanitize(string s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s.Replace('\n', ' ').Replace('\r', ' ');
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleExporter"/> class.
    /// </summary>
    /// <param name="clock">
    /// Provides the current time. If <see langword="null"/>, defaults to <see cref="UtcTimeProvider"/>.
    /// </param>
    /// <param name="consoleWriter">
    /// Provides console output handling. If <see langword="null"/>, defaults to <see cref="ConsoleWriter"/>.
    /// </param>
    /// <remarks>
    /// The <paramref name="clock"/> is used only to obtain a single UTC timestamp for the entire
    /// export batch, ensuring consistent timestamps across all written metrics.
    /// </remarks>
    public ConsoleExporter(ITimeProvider? clock = null, IConsoleWriter? consoleWriter = null)
    {
        _clock = clock ?? new UtcTimeProvider();
        _consoleWriter = consoleWriter ?? new ConsoleWriter();
    }

    /// <summary>
    /// Exports the provided metrics by writing them to the console.
    /// </summary>
    /// <param name="metrics">The metrics to export. Must not be <see langword="null"/>.</param>
    /// <param name="ct">A cancellation token used to observe cancellation.</param>
    /// <returns>
    /// A task that completes once all metrics have been formatted and written to the console output.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each metric is rendered on a new line using the following template:
    /// </para>
    /// <code>
    /// [timestamp] metric.Name (metric.Id) => value tags
    /// </code>
    /// <para>
    /// The timestamp is emitted in round-trip (ISO-8601) format and reflects the exporter clock at
    /// the start of the operation. Tag keys and values are sorted by key (ordinal) and emitted as
    /// <c>key=value</c> pairs separated by commas. Newline characters within tag keys/values are
    /// replaced by spaces to maintain single-line output.
    /// </para>
    /// <para>
    /// If writing to the console fails, a human-readable error is written to <see cref="Console.Error"/>
    /// and processing continues with subsequent metrics. If cancellation is requested, an
    /// <see cref="OperationCanceledException"/> is propagated immediately.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="metrics"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    [RequiresUnreferencedCode("ConsoleExporter.ExportAsync may be called from trimmed apps; make sure metric types are preserved.")]
    public async Task ExportAsync(IEnumerable<IMetric> metrics, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var ci = CultureInfo.InvariantCulture;
        var now = _clock.UtcNow;

        foreach (var metric in metrics)
        {
            ct.ThrowIfCancellationRequested();
            if (metric is null) continue; // defensively skip null items

            var tags = (metric.Tags is { Count: > 0 })
                ? " " + string.Join(
                    ",",
                    metric.Tags!
                          .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                          .Select(kv => $"{Sanitize(kv.Key)}={Sanitize(kv.Value)}"))
                : string.Empty;

            // Value formatting is type-aware. Unknown values fall back to ToString().
            string text = metric.GetValue() switch
            {
                GaugeValue g => g.Value.ToString("0.###", ci),
                CounterValue c => c.Value.ToString(ci),
                DistributionValue d => $"count={d.Count},min={d.Min:0.###},max={d.Max:0.###},p50={d.P50:0.###},p90={d.P90:0.###},p99={d.P99:0.###}",
                SummaryValue s => $"count={s.Count},min={s.Min:0.###},max={s.Max:0.###}," +
                                  string.Join(",", s.Quantiles.OrderBy(kv => kv.Key)
                                      .Select(kv => $"q{kv.Key:0.##}={kv.Value:0.###}")),
                BucketHistogramValue bh => $"count={bh.Count},min={bh.Min:0.###},max={bh.Max:0.###}," +
                                           string.Join(",", bh.Buckets.Select((b, i) => $"le={b:0.###}:{bh.Counts[i]}")) +
                                           $",le=+Inf:{bh.Counts[^1]}",
                MultiSampleValue ms => $"items={ms.Items.Count}",
                var other => other?.ToString() ?? "null"
            };

            try
            {
                await _consoleWriter
                    .WriteLineAsync($"[{now:o}] {metric.Name} ({metric.Id}) => {text}{tags}")
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Preserve cancellation semantics
                throw;
            }
            catch (IOException ioEx)
            {
                // Console.Out/Console.Error are TextWriter-based; IO errors are plausible.
                await Console.Error
                    .WriteLineAsync($"Error writing to console (IO): {ioEx.Message}")
                    .ConfigureAwait(false);
            }
            catch (ObjectDisposedException odEx)
            {
                await Console.Error
                    .WriteLineAsync($"Error writing to console (disposed): {odEx.Message}")
                    .ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Default implementation of <see cref="IConsoleWriter"/> that writes directly to <see cref="Console.Out"/>.
/// </summary>
/// <remarks>
/// <para>
/// This indirection decouples <see cref="ConsoleExporter"/> from the physical console, enabling unit
/// tests to verify formatting and error handling without mutating process standard streams.
/// </para>
/// <para>
/// Thread-safety: multiple concurrent calls to <see cref="WriteLineAsync(string)"/> are supported
/// by the underlying <see cref="TextWriter"/>.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Substitute your own test double for IConsoleWriter:
/// var fake = Substitute.For<IConsoleWriter>();
/// var exporter = new ConsoleExporter(consoleWriter: fake);
/// await exporter.ExportAsync(new[] { Metric.Counter("ops", 1) });
/// await fake.Received().WriteLineAsync(Arg.Is<string>(s => s.Contains("ops")));
/// ]]></code>
/// </example>
public class ConsoleWriter : IConsoleWriter
{
    /// <summary>
    /// Writes a line of text to <see cref="Console.Out"/> asynchronously.
    /// </summary>
    /// <param name="text">The line to write. May be empty but not <see langword="null"/>.</param>
    /// <returns>A task that completes once the line has been written.</returns>
    /// <remarks>
    /// This method delegates to <see cref="TextWriter.WriteLineAsync(string?)"/> on <see cref="Console.Out"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public Task WriteLineAsync(string text)
    {
        // Fully asynchronous behavior via TextWriter.WriteLineAsync
        return Console.Out.WriteLineAsync(text);
    }
}
