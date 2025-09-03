// <copyright file="ModuleLoader.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Registry;

/// <summary>
/// Summary of a module load operation.
/// </summary>
/// <param name="Registered">
/// Number of modules successfully registered (before initialization). This counts successful calls to
/// <see cref="MetricRegistry.RegisterModule(IModule)"/> regardless of whether <c>OnInit</c> later succeeds.
/// </param>
/// <param name="Initialized">
/// Number of modules whose <c>OnInit</c> completed successfully (i.e., <see cref="IModuleLifecycle.OnInit"/> did not throw).
/// </param>
/// <param name="Skipped">
/// Number of items skipped (null entries, filtered out by <see cref="ModuleLoadOptions.ModuleFilter"/>, or
/// already registered when <see cref="ModuleLoadOptions.TreatAlreadyExistsAsSkip"/> is <see langword="true"/>).
/// </param>
/// <param name="Errors">
/// Collection of human-readable error messages produced during registration or initialization. This list includes
/// recoverable registry/lifecycle errors but excludes unexpected exceptions that are rethrown.
/// </param>
/// <param name="Elapsed">Total elapsed wall-clock time for the load operation.</param>
/// <remarks>
/// <para>
/// <see cref="Registered"/> and <see cref="Initialized"/> may differ when initialization fails and rollback occurs.
/// </para>
/// </remarks>
public sealed record LoadSummary(
    int Registered,
    int Initialized,
    int Skipped,
    IReadOnlyList<string> Errors,
    TimeSpan Elapsed);

/// <summary>
/// Options controlling the module loading behavior.
/// </summary>
/// <remarks>
/// <para>
/// The loader is safe-by-default: by using <see cref="Sequential"/> = <see langword="true"/>, it prevents unintended
/// contention in user code during <c>OnInit</c>. For systems that can tolerate concurrency,
/// set <see cref="Sequential"/> to <see langword="false"/> and optionally cap concurrency with
/// <see cref="MaxDegreeOfParallelism"/>.
/// </para>
/// </remarks>
public sealed class ModuleLoadOptions
{
    /// <summary>
    /// If <see langword="true"/> (default), modules are initialized sequentially.
    /// If <see langword="false"/>, modules may be initialized in parallel up to <see cref="MaxDegreeOfParallelism"/>.
    /// </summary>
    /// <remarks>
    /// Prefer the sequential mode unless your <see cref="IModuleLifecycle.OnInit"/> implementations are known to be
    /// thread-safe and free of shared resource contention.
    /// </remarks>
    public bool Sequential { get; init; } = true;

    /// <summary>
    /// Maximum degree of parallelism when <see cref="Sequential"/> is <see langword="false"/>.
    /// Defaults to <c>Environment.ProcessorCount</c> when not specified.
    /// </summary>
    /// <remarks>
    /// Values &lt;= 0 are ignored and treated as <c>Environment.ProcessorCount</c>.
    /// </remarks>
    public int? MaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// When <see langword="true"/> (default), treats <see cref="ErrorCode.AlreadyExists"/> as a non-fatal "skip"
    /// rather than an error. This is convenient for idempotent loaders and repeated calls.
    /// </summary>
    public bool TreatAlreadyExistsAsSkip { get; init; } = true;

    /// <summary>
    /// Optional predicate used to decide whether a module should be considered for registration.
    /// Returning <see langword="false"/> will skip the module and increment <see cref="LoadSummary.Skipped"/>.
    /// </summary>
    public Func<IModule, bool>? ModuleFilter { get; init; }

    /// <summary>
    /// Optional info logger invoked for benign events (e.g., filtered or skipped modules).
    /// </summary>
    public Action<string>? LogInfo { get; init; }

    /// <summary>
    /// Optional error logger invoked for recoverable errors (e.g., registry/lifecycle issues).
    /// </summary>
    public Action<string>? LogError { get; init; }
}

/// <summary>
/// Loads modules into a <see cref="MetricRegistry"/>, optionally filters them, and invokes lifecycle hooks
/// (e.g., <see cref="IModuleLifecycle.OnInit"/>). On initialization failure, the module is unregistered (rollback).
/// </summary>
/// <remarks>
/// <para><b>Safety and rollback:</b> Registration happens first; if <c>OnInit</c> later throws an expected exception,
/// the loader unregisters the module to keep the registry consistent.</para>
/// <para><b>Parallelism:</b> When <see cref="ModuleLoadOptions.Sequential"/> is <see langword="false"/> the loader uses
/// <c>Parallel.ForEach</c> with an optional cap via <see cref="ModuleLoadOptions.MaxDegreeOfParallelism"/>.</para>
/// <para><b>Error semantics:</b> Known, actionable exceptions are logged and summarized in <see cref="LoadSummary.Errors"/>.
/// Unexpected exceptions are rethrown after rollback to preserve diagnostics.</para>
/// </remarks>
/// <example>
/// Basic usage with sequential initialization:
/// <code language="csharp"><![CDATA[
/// var options = new ModuleLoadOptions
/// {
///     ModuleFilter = m => !string.Equals(m.Name, "experimental", StringComparison.OrdinalIgnoreCase)
/// };
///
/// var result = ModuleLoader.LoadModules(registry, modules, options, ct);
/// if (!result.IsSuccess)
/// {
///     Console.Error.WriteLine($"{result.Code}: {result.Error}");
/// }
/// else
/// {
///     var summary = result.Value!;
///     Console.WriteLine($"Registered={summary.Registered}, Init={summary.Initialized}, Skipped={summary.Skipped}, Took={summary.Elapsed}");
///     foreach (var err in summary.Errors) Console.WriteLine(err);
/// }]]>
/// </code>
/// </example>
public static class ModuleLoader
{
    /// <summary>
    /// Loads the provided <paramref name="modules"/> into the given <paramref name="registry"/>,
    /// invoking <c>OnInit</c> where applicable. On initialization failure, the module is unregistered (rollback).
    /// </summary>
    /// <param name="registry">Target registry; must not be <see langword="null"/>.</param>
    /// <param name="modules">Modules to register and initialize; <see langword="null"/> items are skipped.</param>
    /// <param name="options">Behavioral options; <see langword="null"/> to use defaults.</param>
    /// <param name="ct">Cancellation token to abort the load loop early (best-effort under parallelism).</param>
    /// <returns>
    /// <see cref="Result{T}"/> with a <see cref="LoadSummary"/> describing the operation.
    /// On invalid input, returns <see cref="ErrorCode.InvalidArgument"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// If <see cref="ModuleLoadOptions.TreatAlreadyExistsAsSkip"/> is enabled (default), attempts to register a module
    /// whose name already exists are counted as <b>skipped</b>, not as errors.
    /// </para>
    /// <para>
    /// When running in parallel, the loader uses best-effort cancellation; already-started module tasks may complete.
    /// </para>
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// Thrown if <paramref name="ct"/> is canceled before or during a sequential loop,
    /// or propagated from the data-parallel scheduler during parallel execution.
    /// </exception>
    public static Result<LoadSummary> LoadModules(
        MetricRegistry registry,
        IEnumerable<IModule> modules,
        ModuleLoadOptions? options = null,
        CancellationToken ct = default)
    {
        if (registry is null)
        {
            return Result.Failure<LoadSummary>("Registry is null.", ErrorCode.InvalidArgument);
        }

        if (modules is null)
        {
            return Result.Failure<LoadSummary>("Modules is null.", ErrorCode.InvalidArgument);
        }

        options ??= new ModuleLoadOptions();

        var list = modules as ICollection<IModule> ?? modules.ToArray();
        var errors = new ConcurrentBag<string>();
        int registered = 0, initialized = 0, skipped = 0;

        var sw = Stopwatch.StartNew();

        // Local worker encapsulating register+init+rollback.
        void Process(IModule? module)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (module is null)
            {
                Interlocked.Increment(ref skipped);
                errors.Add("Null module entry skipped.");
                return;
            }

            if (options.ModuleFilter is not null && !options.ModuleFilter(module))
            {
                Interlocked.Increment(ref skipped);
                options.LogInfo?.Invoke($"[ModuleSkip] {module.Name} filtered out.");
                return;
            }

            var reg = registry.RegisterModule(module);

            if (!reg.IsSuccess)
            {
                // Respect TreatAlreadyExistsAsSkip when available
                if (options.TreatAlreadyExistsAsSkip && reg.Code == ErrorCode.AlreadyExists)
                {
                    Interlocked.Increment(ref skipped);
                    options.LogInfo?.Invoke($"[RegisterSkip] Module '{module.Name}' already exists.");
                    return;
                }

                errors.Add($"[RegisterError] Module '{module.Name}': {reg.Error}");
                options.LogError?.Invoke($"[RegisterError] Module '{module.Name}': {reg.Error}");
                return;
            }

            Interlocked.Increment(ref registered);

            // Invoke OnInit outside of ModuleRegistry locks (ModuleRegistry already ensures that).
            if (module is IModuleLifecycle lifecycle)
            {
                try
                {
                    lifecycle.OnInit();
                    Interlocked.Increment(ref initialized);
                }
                catch (ObjectDisposedException ex)
                {
                    registry.UnregisterModule(module);
                    errors.Add($"[LifecycleError] Module '{module.Name}', OnInit(ObjectDisposed): {ex.Message}");
                    options.LogError?.Invoke($"[LifecycleError] Module '{module.Name}', OnInit(ObjectDisposed): {ex.Message}");
                }
                catch (InvalidOperationException ex)
                {
                    registry.UnregisterModule(module);
                    errors.Add($"[LifecycleError] Module '{module.Name}', OnInit(InvalidOperation): {ex.Message}");
                    options.LogError?.Invoke($"[LifecycleError] Module '{module.Name}', OnInit(InvalidOperation): {ex.Message}");
                }
                catch (IOException ex)
                {
                    registry.UnregisterModule(module);
                    errors.Add($"[LifecycleError] Module '{module.Name}', OnInit(IO): {ex.Message}");
                    options.LogError?.Invoke($"[LifecycleError] Module '{module.Name}', OnInit(IO): {ex.Message}");
                }
                // Rethrow unexpected exceptions so they are not silently ignored.
                catch
                {
                    // Ensure rollback even for unexpected failures, then rethrow.
                    registry.UnregisterModule(module);
                    throw;
                }
            }
        }

        if (options.Sequential)
        {
            foreach (var m in list)
            {
                ct.ThrowIfCancellationRequested();
                Process(m);
            }
        }
        else
        {
            var dop = Math.Max(1, options.MaxDegreeOfParallelism ?? Environment.ProcessorCount);
            Parallel.ForEach(
                list,
                new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = ct },
                (IModule m) => Process(m));
        }

        sw.Stop();
        var summary = new LoadSummary(registered, initialized, skipped, errors.ToArray(), sw.Elapsed);
        return Result.Success<LoadSummary>(summary);
    }
}
