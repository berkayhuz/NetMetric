// <copyright file="MetricManager.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace NetMetric.Registry;

/// <summary>
/// Coordinates discovery and execution of all metric modules/collectors
/// and hands the collected data off to the configured exporter.
/// </summary>
/// <remarks>
/// <para>
/// This type is <see langword="sealed"/> and uses internal synchronization to prevent
/// overlapping end-to-end collection/export cycles. When
/// <see cref="MetricOptions.EnableParallelCollectors"/> is enabled, individual collectors inside a
/// module may run concurrently while the overall pipeline is still guarded by an internal semaphore.
/// </para>
/// <para><b>Thread safety:</b> All public members are thread-safe. The class serializes
/// <see cref="CollectAndExportAllAsync(System.Threading.CancellationToken)"/> and its overload to
/// ensure only one end-to-end run is active at a time.</para>
/// <para><b>Trimming/AOT:</b> Exporters may use reflection; see
/// <see cref="CollectAndExportAllAsync(System.Threading.CancellationToken)"/> for guidance when
/// publishing with trimming or AOT.</para>
/// </remarks>
public sealed class MetricManager : IDisposable
{
    private readonly MetricRegistry _registry;
    private readonly IOptionsMonitor<MetricOptions> _optionsMon;
    private readonly ITimeProvider _clock;
    private readonly IMetricFactory _factory;
    private readonly SemaphoreSlim _collectGate = new(1, 1);
    private readonly SelfMetricsSet? _self;

    private MetricOptions Options => _optionsMon.CurrentValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="MetricManager"/> class.
    /// </summary>
    /// <param name="registry">The central registry that exposes loaded modules and their collectors.</param>
    /// <param name="optionsMonitor">Live <see cref="MetricOptions"/> source used throughout the pipeline.</param>
    /// <param name="clock">UTC time provider; if <see langword="null"/>, defaults to <see cref="UtcTimeProvider"/>.</param>
    /// <param name="factory">Metric factory used to build internal/self metrics (e.g., pipeline timings).</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="registry"/> or <paramref name="optionsMonitor"/> or <paramref name="factory"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If <see cref="MetricOptions.EnableSelfMetrics"/> is enabled at construction time, a small set of
    /// self-observability metrics (collect/export durations and outcomes) is initialized via
    /// <paramref name="factory"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// The manager is typically resolved from DI:
    /// <code language="csharp"><![CDATA[
    /// using Microsoft.Extensions.DependencyInjection;
    /// using NetMetric.DependencyInjection;
    /// 
    /// var services = new ServiceCollection()
    ///     .AddLogging()
    ///     .AddNetMetricCore(opts =>
    ///     {
    ///         opts.EnableParallelCollectors = true;
    ///         opts.CollectorParallelism     = Environment.ProcessorCount;
    ///         opts.SamplingRate             = 1.0;
    ///         opts.EnableSelfMetrics        = true;
    ///     });
    /// 
    /// using var sp = services.BuildServiceProvider();
    /// var manager = sp.GetRequiredService<MetricManager>();
    /// 
    /// // Run full pipeline (collect -> export) respecting cancellation:
    /// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    /// var result = await manager.CollectAndExportAllAsync(cts.Token);
    /// 
    /// if (!result.IsSuccess)
    ///     Console.Error.WriteLine($"Export failed: {result.Error} (code: {result.Code})");
    /// ]]></code>
    /// </example>
    public MetricManager(
        MetricRegistry registry,
        IOptionsMonitor<MetricOptions> optionsMonitor,
        ITimeProvider? clock = null,
        IMetricFactory? factory = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _optionsMon = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _clock = clock ?? new UtcTimeProvider();
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

        if (Options.EnableSelfMetrics)
            _self = new SelfMetricsSet(_factory, Options);
    }

    /// <summary>
    /// Gets the current UTC timestamp from the configured <see cref="ITimeProvider"/>.
    /// </summary>
    /// <returns>The current <see cref="DateTime"/> in UTC.</returns>
    /// <remarks>
    /// This is a convenience wrapper over <see cref="ITimeProvider.UtcNow"/> mainly intended
    /// for testing and for components that require an injectable time source.
    /// </remarks>
    public DateTime NowUtc() => _clock.UtcNow;

    /// <summary>
    /// Releases managed resources held by this instance (e.g., semaphores and registry).
    /// </summary>
    /// <remarks>
    /// <para>This method is idempotent and safe to call multiple times.</para>
    /// <para>
    /// It does <b>not</b> abort an in-flight collection/export run; use a <see cref="CancellationToken"/>
    /// when invoking <see cref="CollectAndExportAllAsync(System.Threading.CancellationToken)"/>.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        _collectGate.Dispose();
        _registry.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Runs all collectors and exports the collected metrics using the configured exporter.
    /// </summary>
    /// <param name="ct">Cancellation token used both for collection and export stages.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> where <c>T</c> is <see cref="bool"/>: <see langword="true"/> on success; otherwise a failure with an error code.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Only known and actionable exceptions (e.g., cancellations, I/O, disposed objects, invalid operations)
    /// are handled locally and translated into a <see cref="Result{T}"/> with an appropriate
    /// <see cref="ErrorCode"/>. Unexpected exceptions are allowed to bubble up to preserve diagnostics.
    /// </para>
    /// <para>
    /// <b>Trimming/AOT:</b> Marked with <see cref="RequiresUnreferencedCodeAttribute"/> because exporters may rely
    /// on reflection. When publishing with trimming, ensure public members of exported metric types are preserved.
    /// </para>
    /// <para>
    /// <b>Reentrancy:</b> The method uses an internal gate to serialize pipeline runs; concurrent calls queue up.
    /// </para>
    /// </remarks>
    /// <example>
    /// End-to-end collect &amp; export with error handling:
    /// <code language="csharp"><![CDATA[
    /// var res = await manager.CollectAndExportAllAsync(ct);
    /// if (!res.IsSuccess)
    /// {
    ///     // Inspect error details and decide whether to retry or log.
    ///     Console.Error.WriteLine($"{res.Code}: {res.Error}");
    /// }
    /// ]]></code>
    /// </example>
    [RequiresUnreferencedCode("Exporters may use reflection. With trimming enabled, keep public members of metric types.")]
    public async Task<Result<bool>> CollectAndExportAllAsync(CancellationToken ct = default)
    {
        await _collectGate.WaitAsync(ct).ConfigureAwait(false);
        var collectScope = _self?.StartCollect();

        try
        {
            var collected = await CollectAllAsync(ct).ConfigureAwait(false);

            var exportScope = _self?.StartExport();
            try
            {
                await ExportCollectedAsync(collected, ct).ConfigureAwait(false);
                exportScope?.Ok();
            }
            catch (OperationCanceledException) when (Options.CancelOnToken)
            {
                exportScope?.Error();
                return Result.Failure<bool>("Export cancelled.", ErrorCode.Cancelled);
            }
            catch (IOException ex)
            {
                Options.LogError?.Invoke($"[ExporterIO] {ex.Message}");
                exportScope?.Error();
                return Result.Failure<bool>($"Exporter IO error: {ex.Message}", ErrorCode.ExporterError);
            }
            catch (ObjectDisposedException ex)
            {
                Options.LogError?.Invoke($"[ExporterDisposed] {ex.Message}");
                exportScope?.Error();
                return Result.Failure<bool>($"Exporter disposed: {ex.Message}", ErrorCode.ExporterError);
            }
            catch (InvalidOperationException ex)
            {
                Options.LogError?.Invoke($"[ExporterInvalidOp] {ex.Message}");
                exportScope?.Error();
                return Result.Failure<bool>($"Exporter invalid op: {ex.Message}", ErrorCode.ExporterError);
            }

            collectScope?.Ok();
            return Result.Success(true);
        }
        catch (OperationCanceledException) when (Options.CancelOnToken)
        {
            collectScope?.Error();
            return Result.Failure<bool>("Collection cancelled.", ErrorCode.Cancelled);
        }
        finally
        {
            _collectGate.Release();
        }
    }

    /// <summary>
    /// Runs all collectors and writes each formatted metric line-by-line using the supplied delegate
    /// (e.g., to stdout, HTTP streaming response, or a file).
    /// </summary>
    /// <param name="writeLineAsync">An asynchronous writer invoked once per metric.</param>
    /// <param name="ct">Cancellation token applied to collection and to each write call.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> where <c>T</c> is <see cref="bool"/>: <see langword="true"/> on success; otherwise a failure with an error code.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This overload bypasses the configured <see cref="IMetricExporter"/> and gives the caller full control
    /// over the output representation via <paramref name="writeLineAsync"/>.
    /// </para>
    /// <para>
    /// The helper uses a simple <see cref="FormatMetric(IMetric)"/> representation. For structured formats
    /// (e.g., OpenTelemetry, Prometheus), prefer the exporter-based overload or supply a writer that
    /// performs your desired serialization.
    /// </para>
    /// </remarks>
    /// <example>
    /// Stream metrics to the console:
    /// <code language="csharp"><![CDATA[
    /// var res = await manager.CollectAndExportAllAsync(
    ///     async (line, token) =>
    ///     {
    ///         await Console.Out.WriteLineAsync(line);
    ///     },
    ///     ct);
    /// ]]></code>
    /// </example>
    [RequiresUnreferencedCode("Exporters may use reflection. With trimming enabled, keep public members of metric types.")]
    public async Task<Result<bool>> CollectAndExportAllAsync(
        Func<string, CancellationToken, Task> writeLineAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(writeLineAsync);

        await _collectGate.WaitAsync(ct).ConfigureAwait(false);
        var collectScope = _self?.StartCollect();

        try
        {
            var collected = await CollectAllAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var metric in collected)
                {
                    ct.ThrowIfCancellationRequested();
                    await writeLineAsync(FormatMetric(metric), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (Options.CancelOnToken)
            {
                return Result.Failure<bool>("Export cancelled.", ErrorCode.Cancelled);
            }
            catch (IOException ex)
            {
                Options.LogError?.Invoke($"[ExporterIO] {ex.Message}");
                return Result.Failure<bool>($"Exporter IO error: {ex.Message}", ErrorCode.ExporterError);
            }
            catch (ObjectDisposedException ex)
            {
                Options.LogError?.Invoke($"[ExporterDisposed] {ex.Message}");
                return Result.Failure<bool>($"Exporter disposed: {ex.Message}", ErrorCode.ExporterError);
            }
            catch (InvalidOperationException ex)
            {
                Options.LogError?.Invoke($"[ExporterInvalidOp] {ex.Message}");
                return Result.Failure<bool>($"Exporter invalid op: {ex.Message}", ErrorCode.ExporterError);
            }

            collectScope?.Ok();
            return Result.Success(true);
        }
        catch (OperationCanceledException) when (Options.CancelOnToken)
        {
            collectScope?.Error();
            return Result.Failure<bool>("Collection cancelled.", ErrorCode.Cancelled);
        }
        finally
        {
            _collectGate.Release();
        }
    }

    private async Task<IEnumerable<IMetric>> CollectAllAsync(CancellationToken ct)
    {
        var collected = new ConcurrentBag<IMetric>();

        foreach (var module in _registry.Modules)
        {
            if (ct.IsCancellationRequested && Options.CancelOnToken)
                return collected;

            if (module is IModuleLifecycle lifecycle)
                SafeInvoke(() => lifecycle.OnBeforeCollect(), module.Name, nameof(IModuleLifecycle.OnBeforeCollect));

            try
            {
                var collectors = module.GetCollectors()?.ToArray() ?? Array.Empty<IMetricCollector>();
                if (Options.EnableParallelCollectors && collectors.Length > 1)
                    await RunCollectorsParallelAsync(module, collectors, collected, ct).ConfigureAwait(false);
                else
                    await RunCollectorsSequentialAsync(module, collectors, collected, ct).ConfigureAwait(false);
            }
            finally
            {
                if (module is IModuleLifecycle lifecycle2)
                    SafeInvoke(() => lifecycle2.OnAfterCollect(), module.Name, nameof(IModuleLifecycle.OnAfterCollect));
            }
        }

        return collected;
    }

    [RequiresUnreferencedCode("Exporters may use reflection. With trimming enabled, keep public members of metric types.")]
    private async Task ExportCollectedAsync(IEnumerable<IMetric> collected, CancellationToken ct)
    {
        if (Options.Exporter is null)
            return;

        await Options.Exporter.ExportAsync(collected, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Formats a metric into a simple readable line (for ad-hoc streaming scenarios).
    /// </summary>
    /// <param name="metric">The metric to format.</param>
    /// <returns>A string in the shape <c>{Name} = {Value}</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metric"/> is <see langword="null"/>.</exception>
    private static string FormatMetric(IMetric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return $"{metric.Name} = {metric.GetValue()}";
    }

    /// <summary>
    /// Runs collectors sequentially and adds non-filtered results to the sink.
    /// </summary>
    private async Task RunCollectorsSequentialAsync(
        IModule module,
        IMetricCollector[] collectors,
        ConcurrentBag<IMetric> sink,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(collectors);
        ArgumentNullException.ThrowIfNull(sink);

        foreach (var collector in collectors)
        {
            ct.ThrowIfCancellationRequested();
            var metric = await RunCollectorWithTimeoutAsync(module, collector, ct).ConfigureAwait(false);
            if (metric is not null && ShouldInclude(metric))
                sink.Add(metric);
        }
    }

    /// <summary>
    /// Runs collectors in parallel respecting configured degree of parallelism.
    /// </summary>
    private async Task RunCollectorsParallelAsync(
        IModule module,
        IMetricCollector[] collectors,
        ConcurrentBag<IMetric> sink,
        CancellationToken ct)
    {
        int dop = Math.Max(1, Options.CollectorParallelism ?? Environment.ProcessorCount);
        using var semaphore = new SemaphoreSlim(dop, dop);

        var tasks = collectors.Select(async collector =>
        {
            await semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var metric = await RunCollectorWithTimeoutAsync(module, collector, ct).ConfigureAwait(false);
                if (metric is not null && ShouldInclude(metric))
                    sink.Add(metric);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a collector with an optional timeout and handles expected exceptions.
    /// </summary>
    private async Task<IMetric?> RunCollectorWithTimeoutAsync(
        IModule module,
        IMetricCollector collector,
        CancellationToken outerCt)
    {
        ArgumentNullException.ThrowIfNull(collector);
        ArgumentNullException.ThrowIfNull(module);

        try
        {
            var timeoutMs = Options.CollectorTimeoutMs;
            if (timeoutMs <= 0)
                return await collector.CollectAsync(outerCt).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
            cts.CancelAfter(timeoutMs);

            try
            {
                return await collector.CollectAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !outerCt.IsCancellationRequested)
            {
                Options.LogError?.Invoke(
                    $"[CollectTimeout] Module {module.Name}, Collector {collector.GetType().Name}, Timeout {timeoutMs}ms");
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            Options.LogInfo?.Invoke($"[CollectCancelled] Module {module.Name}, Collector {collector.GetType().Name}");
            return null;
        }
        catch (ObjectDisposedException ex)
        {
            Options.LogError?.Invoke($"[CollectDisposed] Module {module.Name}, Collector {collector.GetType().Name}: {ex.Message}");
            return null;
        }
        catch (InvalidOperationException ex)
        {
            Options.LogError?.Invoke($"[CollectInvalidOp] Module {module.Name}, Collector {collector.GetType().Name}: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            Options.LogError?.Invoke($"[CollectIO] Module {module.Name}, Collector {collector.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Applies the optional filter and sampling policy to decide whether a metric is kept.
    /// </summary>
    private bool ShouldInclude(IMetric metric)
    {
        if (Options.MetricFilter is not null && !Options.MetricFilter(metric))
            return false;

        var rate = Options.SamplingRate;
        if (rate < 1.0)
        {
            var next = Options.RandomNextDouble ?? Random.Shared.NextDouble;
            if (next() > rate)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Invokes a lifecycle callback safely, logging only expected exceptions.
    /// </summary>
    private void SafeInvoke(Action? action, string moduleName, string stage)
    {
        if (action is null)
            return;

        try
        {
            action();
        }
        catch (ObjectDisposedException ex)
        {
            Options.LogError?.Invoke($"[LifecycleDisposed] Module {moduleName}, Stage {stage}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            Options.LogError?.Invoke($"[LifecycleInvalidOp] Module {moduleName}, Stage {stage}: {ex.Message}");
        }
    }
}
