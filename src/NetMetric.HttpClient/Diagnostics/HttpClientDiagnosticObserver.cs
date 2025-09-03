// <copyright file="HttpClientDiagnosticObserver.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.HttpClient.Diagnostics;

/// <summary>
/// Observes <see cref="System.Diagnostics.DiagnosticSource"/> listeners to derive client-side HTTP metrics,
/// such as retries, redirects, timeouts, and phase timings (DNS/CONNECT/TLS).
/// </summary>
/// <remarks>
/// <para>
/// This observer subscribes to the <c>System.Net.Http</c>, <c>System.Net.Sockets</c>,
/// <c>System.Net.NameResolution</c>, and <c>System.Net.Security</c> diagnostic listeners and
/// correlates events by the current <see cref="System.Diagnostics.Activity"/> ID.
/// </para>
/// <para>
/// Correlation is best-effort: for requests where the current activity or request message is unavailable,
/// metrics may be omitted. Phase durations are captured using per-activity timestamps and emitted
/// to <see cref="HttpClientMetricSet"/> (histograms/counters as implemented by NetMetric).
/// </para>
/// <para>
/// <strong>Thread safety:</strong> All mutable state is contained in concurrent collections keyed by
/// <see cref="Activity.Id"/> or <see cref="Activity.RootId"/>. The instance is safe to use concurrently
/// across threads observing diagnostic events.
/// </para>
/// </remarks>
/// <example>
/// The following example shows how to register the observer so that it begins listening immediately,
/// and how to ensure it is disposed when the host shuts down:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<HttpClientMetricSet>(sp => CreateHttpClientMetrics(sp));
/// services.AddSingleton<HttpClientDiagnosticObserver>();
/// services.AddHostedService<DiagnosticObserverHostedService>(); // owns disposal
/// ]]></code>
/// </example>
/// <seealso cref="System.Diagnostics.DiagnosticListener"/>
/// <seealso cref="System.Diagnostics.Activity"/>
/// <seealso cref="HttpClientMetricSet"/>
public sealed class HttpClientDiagnosticObserver :
    IObserver<DiagnosticListener>,
    IObserver<KeyValuePair<string, object?>>,
    IDisposable
{
    /// <summary>Root subscription to <see cref="DiagnosticListener.AllListeners"/>.</summary>
    private readonly IDisposable _sub;

    /// <summary>Target metric set used to record counters and observations.</summary>
    private readonly HttpClientMetricSet _metrics;

    /// <summary>Inner subscriptions to individual <see cref="DiagnosticListener"/> instances.</summary>
    private readonly Collection<IDisposable> _innerSubs = new();

    /// <summary>Correlation cache: Activity.Id -&gt; (host, method, scheme) tags.</summary>
    private readonly ConcurrentDictionary<string, (string host, string method, string scheme)> _ctx = new();

    /// <summary>Per-phase start timestamps keyed by (Activity.Id, phase). Phases: dns/connect/tls.</summary>
    private readonly ConcurrentDictionary<(string id, string phase), long> _start = new();

    /// <summary>Heuristic retry counter per root activity (Activity.RootId -&gt; attempt count).</summary>
    private readonly ConcurrentDictionary<string, int> _attempts = new();

    /// <summary>
    /// Initializes a new instance and immediately subscribes to <see cref="DiagnosticListener.AllListeners"/>.
    /// </summary>
    /// <param name="metrics">Metric sink used to emit counters/observations.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="metrics"/> is <see langword="null"/>.</exception>
    public HttpClientDiagnosticObserver(HttpClientMetricSet metrics)
    {
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _sub = DiagnosticListener.AllListeners.Subscribe(this);
    }

    /// <summary>
    /// Called for each discovered <see cref="DiagnosticListener"/>. Subscribes to relevant networking listeners.
    /// </summary>
    /// <param name="listener">The discovered diagnostic listener.</param>
    /// <remarks>
    /// Subscribes when <paramref name="listener"/>.Name is one of:
    /// <c>System.Net.Http</c>, <c>System.Net.Sockets</c>, <c>System.Net.NameResolution</c>, or <c>System.Net.Security</c>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// public void OnNext(DiagnosticListener listener)
    /// {
    ///     // Called by DiagnosticListener.AllListeners subscription.
    /// }
    /// ]]></code>
    /// </example>
    public void OnNext(DiagnosticListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        if (listener.Name is "System.Net.Http"
            or "System.Net.Sockets"
            or "System.Net.NameResolution"
            or "System.Net.Security")
        {
            var d = listener.Subscribe(this);
            lock (_innerSubs)
                _innerSubs.Add(d);
        }
    }

    /// <summary>
    /// Handles individual diagnostic events and maps them to metrics.
    /// </summary>
    /// <param name="evt">The event name and payload pair.</param>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <c>System.Net.Http.HttpRequestOut.Start</c>: captures (host, method, scheme) and increments a retry counter
    /// heuristically per root activity.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>System.Net.Http.ResponseHeaders</c>: increments redirect counter for 3xx statuses.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>System.Net.Http.Exception</c>: increments timeout counter for cancellation-related exceptions.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>System.Net.Http.HttpRequestOut.Stop</c>: clears per-activity state and attempt counters.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// Any <c>*.Start</c>/<c>*.Stop</c> events from NameResolution/Sockets/Security start and stop phase timers
    /// (<c>dns</c>/<c>connect</c>/<c>tls</c>) and observe durations.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <example>
    /// Typical event names produced by .NET networking:
    /// <code language="text"><![CDATA[
    /// System.Net.Http.HttpRequestOut.Start
    /// System.Net.NameResolution.ResolutionStart / ResolutionStop
    /// System.Net.Sockets.ConnectStart / ConnectStop
    /// System.Net.Security.HandshakeStart / HandshakeStop
    /// System.Net.Http.ResponseHeaders
    /// System.Net.Http.HttpRequestOut.Stop
    /// ]]></code>
    /// </example>
    public void OnNext(KeyValuePair<string, object?> evt)
    {
        var name = evt.Key;
        var payload = evt.Value;

        // ---- Http request context (for tags) ----
        if (name is "System.Net.Http.HttpRequestOut.Start")
        {
            var req = Get<HttpRequestMessage>(payload, typeof(HttpRequestMessage), "Request");
            var id = Activity.Current?.Id ?? Guid.NewGuid().ToString("n");

            if (req?.RequestUri is { } uri)
            {
                var host = uri.Host ?? "unknown";
                var scheme = uri.Scheme ?? "http";
                var method = req.Method.Method;
                _ctx[id] = (host, method, scheme);

                // Heuristic retry: increment per root activity
                var root = Activity.Current?.RootId ?? id;
                var n = _attempts.AddOrUpdate(root, 1, (_, old) => old + 1);
                if (n > 1)
                    _metrics.GetRetries(host, method, scheme).Increment();
            }
            return;
        }

        if (name is "System.Net.Http.ResponseHeaders")
        {
            var resp = Get<HttpResponseMessage>(payload, typeof(HttpResponseMessage), "Response");
            var id = Activity.Current?.Id;
            if (id is not null && _ctx.TryGetValue(id, out var t) && resp is not null)
            {
                var status = (int)resp.StatusCode;
                if (status is >= 300 and < 400)
                    _metrics.GetRedirects(t.host, t.method, t.scheme).Increment();
            }
            return;
        }

        if (name is "System.Net.Http.Exception")
        {
            HandleHttpException(payload);
            return;
        }

        if (name is "System.Net.Http.HttpRequestOut.Stop")
        {
            var id = Activity.Current?.Id;
            if (id is not null)
            {
                _ctx.TryRemove(id, out _);
                // When root completes, clear attempts and pending phase timers.
                var root = Activity.Current?.RootId ?? id;
                _attempts.TryRemove(root, out _);
                _start.TryRemove((id, "dns"), out _);
                _start.TryRemove((id, "connect"), out _);
                _start.TryRemove((id, "tls"), out _);
            }
            return;
        }

        // ---- DNS / CONNECT / TLS (Start/Stop) ----
        if (name.EndsWith("Start", StringComparison.Ordinal))
        {
            var phase = PhaseForStart(name);
            if (phase is null)
                return;

            var id = Activity.Current?.Id;
            if (id is null)
                return;

            _start[(id, phase)] = Stopwatch.GetTimestamp();
            return;
        }

        if (name.EndsWith("Stop", StringComparison.Ordinal))
        {
            var phase = PhaseForStop(name);
            if (phase is null)
                return;

            var id = Activity.Current?.Id;
            if (id is null)
                return;

            if (_start.TryRemove((id, phase), out var ts) && _ctx.TryGetValue(id, out var t))
            {
                var ms = (Stopwatch.GetTimestamp() - ts) * 1000.0 / Stopwatch.Frequency;
                _metrics.GetPhase(t.host, t.method, t.scheme, phase).Observe(ms);
            }
        }
    }

    /// <summary>
    /// Handles the <c>System.Net.Http.Exception</c> event in a trimming-safe way.
    /// Only performs type checks on <see cref="Exception"/>; does not access members
    /// that require unreferenced metadata (e.g., <see cref="Exception.TargetSite"/>).
    /// </summary>
    /// <param name="payload">The diagnostic payload object carrying the exception.</param>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Only performs type checks on Exception; avoids members that may require unreferenced metadata.")]
    private void HandleHttpException(object? payload)
    {
        var ex = Get<Exception>(payload, typeof(Exception), "Exception");
        var id = Activity.Current?.Id;

        if (id is not null && _ctx.TryGetValue(id, out var t) && ex is not null)
        {
            if (IsCancellation(ex))
                _metrics.GetTimeouts(t.host, t.method, t.scheme).Increment();
        }
    }

    /// <summary>
    /// Determines whether an exception represents a timeout/cancellation condition typical for HTTP operations.
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><see langword="true"/> if the exception indicates cancellation; otherwise, <see langword="false"/>.</returns>
    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException
        or TaskCanceledException
        || (ex is HttpRequestException hre && hre.InnerException is OperationCanceledException);

    /// <summary>
    /// Maps a <c>*Start</c> event name to a canonical phase identifier.
    /// </summary>
    /// <param name="evt">The fully-qualified diagnostic event name.</param>
    /// <returns>
    /// <c>"dns"</c> for NameResolution, <c>"connect"</c> for Sockets, <c>"tls"</c> for Security; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// Event names vary between framework versions; this method uses case-insensitive substring checks.
    /// </remarks>
    private static string? PhaseForStart(string evt)
    {
        // NameResolution, Sockets, Security naming varies slightly across versions; use contains checks.
        ArgumentNullException.ThrowIfNull(evt);

        if (evt.Contains("NameResolution", StringComparison.OrdinalIgnoreCase))
        {
            return "dns";
        }
        if (evt.Contains("Sockets", StringComparison.OrdinalIgnoreCase))
        {
            return "connect";
        }
        if (evt.Contains("Security", StringComparison.OrdinalIgnoreCase))
        {
            return "tls";
        }

        return null;
    }

    /// <summary>
    /// Derives the matching phase for a <c>*Stop</c> event by reusing <see cref="PhaseForStart(string)"/>.
    /// </summary>
    /// <param name="evt">The fully-qualified diagnostic event name for the stop event.</param>
    /// <returns>The phase identifier or <see langword="null"/> if not recognized.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="evt"/> is <see langword="null"/> or empty.</exception>
    public static string? PhaseForStop(string evt)
    {
        ArgumentException.ThrowIfNullOrEmpty(evt);
        return PhaseForStart(evt.Replace(".Stop", ".Start", StringComparison.Ordinal));
    }

    /// <summary>
    /// Extracts a strongly-typed public property from a payload object using reflection.
    /// </summary>
    /// <typeparam name="T">Expected return type of the property.</typeparam>
    /// <param name="payload">The object instance that holds the property.</param>
    /// <param name="payloadType">The compile-time type of the payload, used to safely access public properties.</param>
    /// <param name="prop">The name of the public instance property to extract.</param>
    /// <returns>The extracted value cast to <typeparamref name="T"/> or <see langword="null"/> if unavailable.</returns>
    /// <remarks>
    /// This helper is <em>trimming-friendly</em> by annotating <paramref name="payloadType"/> with
    /// <see cref="DynamicallyAccessedMemberTypes.PublicProperties"/> to preserve necessary metadata.
    /// </remarks>
    private static T? Get<T>(
        object? payload,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type payloadType,
        string prop) where T : class
    {
        ArgumentNullException.ThrowIfNull(prop);
        ArgumentNullException.ThrowIfNull(payloadType);

        if (payload is null)
            return null;

        var p = payloadType.GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
        return p?.GetValue(payload) as T;
    }

    /// <summary>
    /// Reports an error raised by the observable pipeline. No-op.
    /// </summary>
    /// <param name="error">The exception encountered during observation.</param>
    /// <remarks>
    /// Errors are intentionally ignored here to avoid introducing logging dependencies
    /// from the diagnostics pipeline; host-level logging can observe failures if needed.
    /// </remarks>
    public void OnError(Exception error)
    {
        // intentionally no-op: logging removed
    }

    /// <summary>
    /// Signals completion from the observable pipeline. No-op.
    /// </summary>
    public void OnCompleted()
    {
    }

    /// <summary>
    /// Disposes the observer and all inner diagnostic subscriptions.
    /// </summary>
    /// <remarks>
    /// Ensures that the root subscription and any listener subscriptions are released.
    /// Safe to call multiple times; subsequent calls dispose already-disposed subscriptions.
    /// </remarks>
    public void Dispose()
    {
        _sub.Dispose();
        lock (_innerSubs)
        {
            foreach (var d in _innerSubs)
            {
                d.Dispose();
            }
        }
    }
}
