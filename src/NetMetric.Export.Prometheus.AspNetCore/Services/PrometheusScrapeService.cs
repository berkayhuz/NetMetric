using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using NetMetric.Abstractions;
using NetMetric.Export.Prometheus.Options;

namespace NetMetric.Export.Prometheus.AspNetCore.Services;

/// <summary>
/// Coordinates metric collection for Prometheus scrapes by querying all registered <see cref="IModule"/> instances.
/// Concurrency is bounded by <see cref="PrometheusExporterOptions.MaxConcurrentScrapes"/>.
/// Reflection is used to discover common collection method names on modules.
/// </summary>
/// <remarks>
/// <para>
/// This service centralizes the collection phase of a Prometheus scrape. It attempts to call well-known
/// method names on each <see cref="IModule"/> to obtain metrics, supporting both synchronous and asynchronous
/// patterns without imposing a hard interface on module authors.
/// </para>
/// <para>
/// The following method names are probed on each module, in order:
/// </para>
/// <list type="number">
///   <item>
///     <description>
///       Synchronous, parameterless methods returning <c>IEnumerable&lt;IMetric&gt;</c>:
///       <c>GetMetrics</c>, <c>Collect</c>, <c>Snapshot</c>, <c>Enumerate</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       Asynchronous methods returning <c>Task&lt;IEnumerable&lt;IMetric&gt;&gt;</c> (optionally accepting a single <see cref="CancellationToken"/>):
///       <c>CollectAsync</c>, <c>CaptureAsync</c>.
///     </description>
///   </item>
/// </list>
/// <para>
/// Concurrency is controlled by a <see cref="SemaphoreSlim"/> sized to
/// <see cref="PrometheusExporterOptions.MaxConcurrentScrapes"/>. If that value is <c>&lt; 1</c> or the options instance
/// is <see langword="null"/>, a conservative default of <c>1</c> is used.
/// </para>
/// <para>
/// <b>Thread safety:</b> Multiple callers may invoke <see cref="CollectAsync(CancellationToken)"/> concurrently.
/// Calls beyond the configured limit will wait until a slot is available.
/// </para>
/// <para>
/// <b>Error behavior:</b> Exceptions thrown by module methods will propagate to the caller. Since methods are invoked
/// via reflection, exceptions thrown by the target method may surface as <see cref="TargetInvocationException"/>
/// with the original exception in <see cref="Exception.InnerException"/>.
/// </para>
/// <para>
/// <b>Trimming/AOT:</b> This type uses reflection to locate and invoke public methods on module types. When publishing
/// with trimming or AOT, ensure those members are preserved (e.g., via the linker XML, <c>DynamicDependency</c>, or by
/// keeping the methods public). See <see cref="RequiresUnreferencedCodeAttribute"/> on <see cref="CollectAsync(CancellationToken)"/>.
/// </para>
/// </remarks>
/// <example>
/// <para>Basic usage to collect from two modules:</para>
/// <code language="csharp"><![CDATA[
/// var modules = new IModule[] { new CpuModule(), new GcModule() };
/// var options = new PrometheusExporterOptions
/// {
/// MaxConcurrentScrapes = 2
/// };
/// 
/// await using var scrapeService = new PrometheusScrapeService(modules, options);
/// 
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// IEnumerable<IMetric> snapshot = await scrapeService.CollectAsync(cts.Token);
///
/// Forward 'snapshot' to your Prometheus formatter/exporter...
/// ]]></code>
/// </example>
/// <example>
/// <para>Authoring a module that this service can discover:</para>
/// <code language="csharp"><![CDATA[
/// public sealed class CpuModule : IModule
/// {
///     // Synchronous pattern
///     public IEnumerable<IMetric> GetMetrics()
///     {
///         yield return Metric.Gauge("process_cpu_percent", 3.14, tags: new { core = "0" });
///     }
/// 
///     // Alternatively, asynchronous pattern (optional CancellationToken)
///     public Task<IEnumerable<IMetric>> CollectAsync(CancellationToken ct)
///     {
///         IEnumerable<IMetric> metrics = new[]
///         {
///             Metric.Gauge("process_cpu_percent", 3.14)
///         };
///         return Task.FromResult(metrics);
///     }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="IModule"/>
/// <seealso cref="IMetric"/>
/// <seealso cref="PrometheusExporterOptions"/>
/// <seealso cref="RequiresUnreferencedCodeAttribute"/>
public sealed class PrometheusScrapeService : IDisposable, IAsyncDisposable
{
    private readonly IModule[] _modules; // snapshot of modules
    private readonly SemaphoreSlim _gate;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PrometheusScrapeService"/> class.
    /// </summary>
    /// <param name="modules">The registered modules that may produce metrics.</param>
    /// <param name="options">Exporter options controlling scrape concurrency and behavior.</param>
    /// <exception cref="ArgumentNullException"><paramref name="modules"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The sequence in <paramref name="modules"/> is materialized into an array to avoid repeated enumeration costs.
    /// If <paramref name="options"/> is <see langword="null"/> or <see cref="PrometheusExporterOptions.MaxConcurrentScrapes"/> is less than <c>1</c>,
    /// a single concurrent scrape is allowed.
    /// </remarks>
    public PrometheusScrapeService(
        IEnumerable<IModule> modules,
        PrometheusExporterOptions? options)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _modules = modules as IModule[] ?? modules.ToArray();

        var max = Math.Max(1, options?.MaxConcurrentScrapes ?? 1);
        _gate = new SemaphoreSlim(max, max);
    }

    /// <summary>
    /// Collects metrics from all registered modules, respecting the configured concurrency limit.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for the operation to complete.</param>
    /// <returns>All non-<see langword="null"/> metrics produced by discovered synchronous or asynchronous module methods.</returns>
    /// <exception cref="OperationCanceledException">The operation was canceled via <paramref name="ct"/>.</exception>
    /// <remarks>
    /// <para>
    /// Probes the following synchronous method names returning <c>IEnumerable&lt;IMetric&gt;</c>:
    /// <c>GetMetrics</c>, <c>Collect</c>, <c>Snapshot</c>, <c>Enumerate</c>.
    /// If none are found, probes asynchronous methods returning <c>Task&lt;IEnumerable&lt;IMetric&gt;&gt;</c>:
    /// <c>CollectAsync</c>, <c>CaptureAsync</c>. If a single <see cref="CancellationToken"/> parameter is present,
    /// the provided <paramref name="ct"/> is supplied.
    /// </para>
    /// <para>
    /// Exceptions thrown by a module method will propagate to the caller. When invoked via reflection,
    /// the runtime may wrap the underlying exception in <see cref="TargetInvocationException"/>.
    /// </para>
    /// </remarks>
    [RequiresUnreferencedCode(
        "Uses reflection to discover/invoke module methods. Under trimming/AOT, make sure module public methods are preserved.")]
    public async Task<IEnumerable<IMetric>> CollectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var all = new Collection<IMetric>();

            foreach (var module in _modules)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var t = module.GetType(); // pass this to helpers so trimmer sees the Type flow

                    if (TryInvokeSync(module, t, "GetMetrics", out var list) ||
                        TryInvokeSync(module, t, "Collect", out list) ||
                        TryInvokeSync(module, t, "Snapshot", out list) ||
                        TryInvokeSync(module, t, "Enumerate", out list))
                    {
                        if (list is not null)
                        {
                            AppendNonNull(all, list);
                        }
                        continue;
                    }

                    if (TryInvokeAsync(module, t, "CollectAsync", ct, out var taskList) ||
                        TryInvokeAsync(module, t, "CaptureAsync", ct, out taskList))
                    {
                        if (taskList is not null)
                        {
                            AppendNonNull(all, await taskList.ConfigureAwait(false));
                        }
                        continue;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Intentionally rethrow to surface module/reflective errors to the caller.
                    throw;
                }
            }

            return all;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Appends non-<see langword="null"/> metrics from <paramref name="src"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The collection receiving metrics.</param>
    /// <param name="src">The source sequence whose non-<see langword="null"/> items will be appended.</param>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="src"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendNonNull(Collection<IMetric> target, IEnumerable<IMetric> src)
    {
        ArgumentNullException.ThrowIfNull(src);
        ArgumentNullException.ThrowIfNull(target);

        foreach (var m in src)
        {
            if (m is not null)
            {
                target.Add(m);
            }
        }
    }

    /// <summary>
    /// Tries to invoke a synchronous, parameterless method returning <see cref="IEnumerable{T}"/> of <see cref="IMetric"/>.
    /// </summary>
    /// <param name="module">The module instance to invoke on.</param>
    /// <param name="type">The runtime type of <paramref name="module"/>. Public methods must be preserved when trimming.</param>
    /// <param name="methodName">The public instance method name to invoke.</param>
    /// <param name="result">Returned metrics if the call succeeded; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the method was found and returned a compatible result; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="methodName"/> is <see langword="null"/> or empty.</exception>
    [RequiresUnreferencedCode("Uses reflection. Ensure the target public method is preserved under trimming.")]
    private static bool TryInvokeSync(
        IModule module,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        string methodName,
        out IEnumerable<IMetric>? result)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(methodName);

        result = null;

        var mi = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (mi is null || mi.GetParameters().Length != 0)
        {
            return false;
        }

        var ret = mi.Invoke(module, null);
        result = ret as IEnumerable<IMetric>;
        return result is not null;
    }

    /// <summary>
    /// Tries to invoke an asynchronous method returning <see cref="Task{TResult}"/> where <c>TResult</c> is
    /// <see cref="IEnumerable{T}"/> of <see cref="IMetric"/>, optionally accepting a single <see cref="CancellationToken"/>.
    /// </summary>
    /// <param name="module">The module instance to invoke on.</param>
    /// <param name="type">The runtime type of <paramref name="module"/>. Public methods must be preserved when trimming.</param>
    /// <param name="methodName">The public instance method name to invoke.</param>
    /// <param name="ct">Cancellation token to pass if the method declares a single <see cref="CancellationToken"/> parameter.</param>
    /// <param name="result">Returned task if the call succeeded; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the method was found and returned a compatible task; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="module"/> or <paramref name="type"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="methodName"/> is <see langword="null"/> or empty.</exception>
    [RequiresUnreferencedCode("Uses reflection. Ensure the target public method is preserved under trimming.")]
    private static bool TryInvokeAsync(
        IModule module,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type type,
        string methodName,
        CancellationToken ct,
        out Task<IEnumerable<IMetric>>? result)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(methodName);

        result = null;

        var mi = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (mi is null)
        {
            return false;
        }

        var ps = mi.GetParameters();
        object?[] args = ps.Length switch
        {
            0 => Array.Empty<object?>(),
            1 when ps[0].ParameterType == typeof(CancellationToken) => new object?[] { ct },
            _ => Array.Empty<object?>()
        };

        var ret = mi.Invoke(module, args);
        result = ret as Task<IEnumerable<IMetric>>;
        return result is not null;
    }

    /// <summary>
    /// Releases resources used by the service.
    /// </summary>
    /// <remarks>
    /// Disposes the underlying <see cref="SemaphoreSlim"/> and suppresses finalization.
    /// Safe to call multiple times.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Asynchronously releases resources used by this instance.
    /// </summary>
    /// <returns>A completed <see cref="ValueTask"/>.</returns>
    /// <remarks>
    /// Delegates to <see cref="Dispose"/> as there are no asynchronous cleanup actions required.
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
