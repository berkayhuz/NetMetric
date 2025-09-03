// <copyright file="MetricRegistry.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Threading;

namespace NetMetric.Registry;

/// <summary>
/// Thread-safe central registry that holds metric <see cref="IModule"/> instances
/// and provides O(1) registration/unregistration by unique module name.
/// </summary>
/// <remarks>
/// <para>
/// The registry is intended to be long-lived and shared across the process. All public members are
/// thread-safe. Internally, a single lock protects structural mutations while enumeration is
/// performed against snapshots returned by <see cref="Modules"/> to avoid holding locks during user iteration.
/// </para>
/// <para><b>Design goals:</b></para>
/// <list type="bullet">
///   <item><description><b>O(1)</b> register/unregister by unique name (via <see cref="KeyedCollection{TKey,TItem}"/>).</description></item>
///  <item><description>Each<see cref = "Modules" /> access returns a<b>snapshot</b> to enumerate safely under concurrency.</description></item>
///   <item><description>Lifecycle callbacks (e.g., <c>OnDispose</c>) are executed <b>outside</b> of locks to avoid deadlocks.</description></item>
///   <item><description>Guards against double-dispose; a module’s <c>OnDispose</c> is invoked at most once.</description></item>
/// </list>
/// </remarks>
/// <example>
/// Typical usage with a simple custom module:
/// <code language="csharp"><![CDATA[
/// public sealed class CpuModule : IModule, IModuleLifecycle
/// {
///     public string Name => "cpu";
///     public IEnumerable<IMetricCollector> GetCollectors() => new[] { new CpuCollector() };
///     public void OnDispose() { /* release unmanaged resources */ }
/// }
///
/// var registry = new MetricRegistry
/// {
///     LogInfo = msg => Console.WriteLine(msg),
///     LogError = msg => Console.Error.WriteLine(msg)
/// };
///
/// // Register
/// var add = registry.RegisterModule(new CpuModule());
/// if (!add.IsSuccess) throw new InvalidOperationException(add.Error);
///
/// // Enumerate a snapshot (safe under concurrency)
/// foreach (var m in registry.Modules)
///     Console.WriteLine(m.Name);
///
/// // Unregister by name (calls OnDispose once if implemented)
/// var remove = registry.UnregisterModule("cpu");
/// if (!remove.IsSuccess) Console.Error.WriteLine(remove.Error);
///
/// registry.Dispose(); // Idempotent; disposes remaining modules once
/// ]]></code>
/// </example>
public sealed class MetricRegistry : IDisposable
{
    // Single gate for protecting _modules/_disposedModules and disposed flag reads.
    private readonly object _gate = new object();

    // Keep insertion order and enable O(1) lookup by module.Name via KeyedCollection.
    private readonly ModuleCollection _modules = new(StringComparer.Ordinal);

    // Track which modules have had OnDispose called exactly once.
    private readonly HashSet<IModule> _disposedModules = new(ReferenceEqualityComparer<IModule>.Instance);

    // Protects against multiple Dispose calls on the registry itself.
    private int _isDisposed;

    /// <summary>
    /// Returns a snapshot of the currently registered modules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each call returns a new array snapshot so that consumers can enumerate without holding
    /// the registry lock. The snapshot is consistent at the moment of acquisition but does not
    /// reflect subsequent mutations.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<IModule> Modules
    {
        get
        {
            lock (_gate)
            {
                // Snapshot to allow lock-free enumeration by consumers.
                return _modules.ToArray();
            }
        }
    }

    /// <summary>
    /// Optional error logger (can be set by library consumers).
    /// </summary>
    /// <remarks>Used to report recoverable lifecycle errors during module disposal.</remarks>
    public Action<string>? LogError { get; init; }

    /// <summary>
    /// Optional info logger (can be set by library consumers).
    /// </summary>
    /// <remarks>Used to report benign informational messages (e.g., cancellations).</remarks>
    public Action<string>? LogInfo { get; init; }

    /// <summary>
    /// Registers a module with a unique name.
    /// </summary>
    /// <param name="module">The module to register. Must expose a non-empty <see cref="IModule.Name"/>.</param>
    /// <returns>
    /// <see cref="Result{T}"/> where <c>T</c> is <see cref="bool"/>:
    /// <see langword="true"/> on success; otherwise a failure with an <see cref="ErrorCode"/>.
    /// </returns>
    /// <remarks>
    /// Registration preserves insertion order for stable enumeration and enables O(1) lookup by name.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var result = registry.RegisterModule(myModule);
    /// if (!result.IsSuccess)
    ///     Console.Error.WriteLine($"{result.Code}: {result.Error}");
    /// ]]></code>
    /// </example>
    public Result<bool> RegisterModule(IModule module)
    {
        if (module is null)
            return Result.Failure<bool>("Module is null.", ErrorCode.InvalidArgument);

        if (string.IsNullOrWhiteSpace(module.Name))
            return Result.Failure<bool>("Module.Name is null or empty.", ErrorCode.InvalidArgument);

        lock (_gate)
        {
            if (_isDisposed == 1)
                return Result.Failure<bool>("Registry is disposed.", ErrorCode.InvalidState);

            if (_modules.Contains(module.Name))
                return Result.Failure<bool>($"Module '{module.Name}' is already registered.", ErrorCode.AlreadyExists);

            _modules.Add(module);
            return Result.Success<bool>(true);
        }
    }

    /// <summary>
    /// Unregisters a module by instance and invokes its lifecycle dispose once (if implemented).
    /// </summary>
    /// <param name="module">The module instance previously registered via <see cref="RegisterModule(IModule)"/>.</param>
    /// <returns>
    /// <see cref="Result{T}"/> with <see cref="bool"/> payload:
    /// <see langword="true"/> on success; otherwise a failure with an <see cref="ErrorCode"/>.
    /// </returns>
    /// <remarks>
    /// Disposal (if supported through <see cref="IModuleLifecycle"/>) is performed outside of locks.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var res = registry.UnregisterModule(module);
    /// if (!res.IsSuccess) Console.Error.WriteLine(res.Error);
    /// ]]></code>
    /// </example>
    public Result<bool> UnregisterModule(IModule module)
    {
        if (module is null)
            return Result.Failure<bool>("Module is null.", ErrorCode.InvalidArgument);

        bool removed;

        lock (_gate)
        {
            if (_isDisposed == 1)
                return Result.Failure<bool>("Registry is disposed.", ErrorCode.InvalidState);

            removed = _modules.Remove(module);
            if (!removed)
                return Result.Failure<bool>($"Module '{module.Name}' is not registered.", ErrorCode.NotFound);
        }

        // Perform potentially heavy/lifecycle work outside the lock.
        TryDisposeModule(module);

        return Result.Success<bool>(true);
    }

    /// <summary>
    /// Unregisters a module by name and invokes its lifecycle dispose once (if implemented).
    /// </summary>
    /// <param name="name">The unique module name used at registration time.</param>
    /// <returns>
    /// <see cref="Result{T}"/> with <see cref="bool"/> payload:
    /// <see langword="true"/> on success; otherwise a failure with an <see cref="ErrorCode"/>.
    /// </returns>
    /// <remarks>
    /// Disposal (if supported through <see cref="IModuleLifecycle"/>) is performed outside of locks.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var res = registry.UnregisterModule("cpu");
    /// if (!res.IsSuccess) Console.Error.WriteLine($"{res.Code}: {res.Error}");
    /// ]]></code>
    /// </example>
    public Result<bool> UnregisterModule(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<bool>("Module name is null or empty.", ErrorCode.InvalidArgument);

        IModule? removed = null;

        lock (_gate)
        {
            if (_isDisposed == 1)
                return Result.Failure<bool>("Registry is disposed.", ErrorCode.InvalidState);

            if (!_modules.Contains(name))
                return Result.Failure<bool>($"Module '{name}' is not registered.", ErrorCode.NotFound);

            removed = _modules[name];
            _modules.Remove(name);
        }

        if (removed is not null)
            TryDisposeModule(removed);

        return Result.Success<bool>(true);
    }

    /// <summary>
    /// Disposes the registry and calls <c>OnDispose</c> for all registered modules (once each).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operation is idempotent. Subsequent calls are no-ops. Disposal of individual modules is
    /// performed outside of locks and guarded so each module receives at most one <c>OnDispose</c> call.
    /// </para>
    /// <para>
    /// After disposal, all registration APIs return <see cref="ErrorCode.InvalidState"/>.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        IModule[] snapshot;

        // Take a snapshot and clear state under lock.
        lock (_gate)
        {
            snapshot = _modules.ToArray();
            _modules.Clear();
        }

        // Perform lifecycle disposal outside the lock.
        foreach (var module in snapshot)
            TryDisposeModule(module);

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Attempts to dispose the given module safely.
    /// Ensures disposal happens only once per module and logs recoverable errors.
    /// </summary>
    /// <param name="module">The target module.</param>
    /// <remarks>
    /// If the module implements <see cref="IModuleLifecycle"/>, its <c>OnDispose</c> is invoked.
    /// Expected exceptions (e.g., <see cref="ObjectDisposedException"/>, <see cref="InvalidOperationException"/>,
    /// <see cref="IOException"/>) are logged via <see cref="LogError"/> and suppressed. Unknown exceptions bubble up.
    /// </remarks>
    private void TryDisposeModule(IModule module)
    {
        // Ensure single OnDispose call per module.
        lock (_gate)
        {
            if (_disposedModules.Contains(module))
                return;

            _disposedModules.Add(module);
        }

        if (module is IModuleLifecycle lifecycle)
        {
            try
            {
                lifecycle.OnDispose();
            }
            catch (ObjectDisposedException ex)
            {
                LogError?.Invoke($"[DisposeError] Module {module.Name} already disposed: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                LogError?.Invoke($"[DisposeError] Module {module.Name} invalid state: {ex.Message}");
            }
            catch (IOException ex)
            {
                LogError?.Invoke($"[DisposeError] Module {module.Name} IO issue: {ex.Message}");
            }
            // Rethrow unexpected exceptions so they are not silently ignored.
            catch
            {
                throw;
            }
        }
    }

    /// <summary>
    /// Backing collection that keeps insertion order and provides O(1) lookups by module name.
    /// </summary>
    private sealed class ModuleCollection : KeyedCollection<string, IModule>
    {
        /// <summary>
        /// Initializes a new <see cref="ModuleCollection"/> that always creates the internal dictionary
        /// to ensure O(1) key lookups regardless of size.
        /// </summary>
        /// <param name="comparer">The string comparer used for module names (e.g., <see cref="StringComparer.Ordinal"/>).</param>
        public ModuleCollection(IEqualityComparer<string> comparer)
            // dictionaryCreationThreshold: 0 => always create the dictionary for O(1) lookups.
            : base(comparer, dictionaryCreationThreshold: 0)
        {
        }

        /// <inheritdoc />
        protected override string GetKeyForItem(IModule item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return item.Name;
        }
    }

    /// <summary>
    /// Reference-equality comparer for use in <see cref="HashSet{T}"/>/<see cref="Dictionary{TKey,TValue}"/>
    /// where instance identity (not value equality) is required.
    /// </summary>
    /// <typeparam name="T">Reference type constrained to <see cref="ReferenceEqualityComparer"/>.</typeparam>
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        /// <summary>
        /// Gets a shared singleton instance of the comparer.
        /// </summary>
        public static readonly ReferenceEqualityComparer<T> Instance = new();

        /// <inheritdoc/>
        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        /// <inheritdoc/>
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
