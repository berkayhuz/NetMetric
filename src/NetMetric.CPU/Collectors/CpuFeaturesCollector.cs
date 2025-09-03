// <copyright file="CpuFeaturesCollector.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.CPU.Collectors;

/// <summary>
/// Collects information about CPU instruction set features that are available to the current process,
/// emitting an <c>info</c>-style gauge metric with <em>tags</em> describing platform and feature support.
/// </summary>
/// <remarks>
/// <para>
/// This collector inspects <see cref="System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture"/> and,
/// based on the active architecture, checks feature flags exposed by <c>System.Runtime.Intrinsics</c> (e.g., SSE, AVX on x86/x64;
/// AdvSimd, CRC32 on Arm64). The result is exported as a gauge metric with value <c>1</c> and a set of descriptive tags.
/// </para>
/// <para>
/// The metric has the following characteristics:
/// </para>
/// <list type="bullet">
///   <item>
///     <description><strong>Metric kind:</strong> Gauge (<c>info</c>-style; value is always <c>1</c> on success).</description>
///   </item>
///   <item>
///     <description><strong>Metric ID:</strong> <c>cpu_features_info</c></description>
///   </item>
///   <item>
///     <description><strong>Metric Name:</strong> <c>CPU Features (info)</c></description>
///   </item>
///   <item>
///     <description>
///       <strong>Tags (always present):</strong>
///       <list type="table">
///         <listheader>
///           <term>Tag</term>
///           <description>Description</description>
///         </listheader>
///         <item>
///           <term><c>arch</c></term>
///           <description>Process architecture (e.g., <c>X64</c>, <c>X86</c>, <c>Arm64</c>).</description>
///         </item>
///         <item>
///           <term><c>os</c></term>
///           <description>Shortened OS description (up to 160 characters).</description>
///         </item>
///         <item>
///           <term><c>osPlatform</c></term>
///           <description>Operating system family (<c>Windows</c>, <c>Linux</c>, <c>macOS</c>, or <c>Unknown</c>).</description>
///         </item>
///         <item>
///           <term><c>is64bit</c></term>
///           <description><c>true</c> if the current process is 64-bit; otherwise <c>false</c>.</description>
///         </item>
///         <item>
///           <term><c>status</c></term>
///           <description><c>ok</c> on success; <c>cancelled</c> or <c>error</c> on failure.</description>
///         </item>
///       </list>
///     </description>
///   </item>
///   <item>
///     <description>
///       <strong>Architecture-specific tags (present when applicable):</strong>
///       <list type="bullet">
///         <item>
///           <description>
///             <em>x86/x64:</em> <c>SSE2</c>, <c>SSE41</c>, <c>SSE42</c>, <c>POPCNT</c>, <c>AES</c>, <c>PCLMUL</c>, <c>AVX</c>, <c>AVX2</c>,
/// #if NET9_0_OR_GREATER
///             <c>AVX512F</c>,
/// #endif
///             <c>BMI1</c>, <c>BMI2</c>, <c>FMA</c>.
///           </description>
///         </item>
///         <item>
///           <description>
///             <em>Arm64:</em> <c>ArmBase</c>, <c>AdvSimd</c>, <c>CRC32</c>
/// #if NET9_0_OR_GREATER
///             , <c>AES</c>, <c>SHA1</c>, <c>SHA256</c>, <c>DOTPROD</c>, <c>AdvSimd64</c>
/// #endif
///             .
///           </description>
///         </item>
///       </list>
///     </description>
///   </item>
/// </list>
/// <para>
/// <strong>Error semantics:</strong> the collector does not throw. On <see cref="System.OperationCanceledException"/> it emits
/// <c>status=cancelled</c> with value <c>0</c>; on any other exception it emits <c>status=error</c> with value <c>0</c> and
/// a <c>reason</c> tag containing a shortened error message.
/// </para>
/// <para>
/// <strong>Thread safety:</strong> This type is stateless with respect to collection; instances are safe to use in concurrent collection
/// scenarios provided the underlying <see cref="IMetricFactory"/> implementation is thread-safe.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// // Registration (e.g., in composition root)
/// IMetricFactory factory = /* resolve from DI */;
/// var collector = new CpuFeaturesCollector(factory);
///
/// // Collect once (e.g., during startup diagnostics) or on a schedule
/// using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
/// var metric = await collector.CollectAsync(cts.Token);
///
/// if (metric is IGauge cpuInfo)
/// {
///     // Value is 1 on success; check tags for details
///     var value = cpuInfo.Value; // 1 or 0
///     var tags = cpuInfo.Tags;   // contains arch/os/features/status
/// }
/// ]]></code>
/// </example>
public sealed class CpuFeaturesCollector : IMetricCollector
{
    private readonly IMetricFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CpuFeaturesCollector"/> class.
    /// </summary>
    /// <param name="factory">The metric factory used to construct the exported gauge.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="factory"/> is <see langword="null"/>.</exception>
    public CpuFeaturesCollector(IMetricFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
#pragma warning disable CA1031
    /// <summary>
    /// Collects CPU feature information and exports it as an <c>info</c>-style gauge metric with descriptive tags.
    /// </summary>
    /// <param name="ct">A token to observe while waiting for completion.</param>
    /// <returns>
    /// A task that completes with the constructed <see cref="IMetric"/> instance. The metric value is <c>1</c> on success,
    /// or <c>0</c> when cancelled or when an error occurs. The <c>status</c> tag indicates <c>ok</c>, <c>cancelled</c>, or <c>error</c>.
    /// </returns>
    /// <remarks>
    /// This method never throws. Cancellation and failures are reflected in the returned metric’s <c>status</c> tag and value.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var metric = await collector.CollectAsync(ct);
    /// // Inspect tags to determine available features:
    /// // e.g., tags["AVX2"] == "true" on capable x64 systems.
    /// ]]></code>
    /// </example>
    public Task<IMetric?> CollectAsync(CancellationToken ct = default)
    {
        const string id = "cpu_features_info"; // info-style metric (value==1)
        const string name = "CPU Features (info)";

        try
        {
            ct.ThrowIfCancellationRequested();

            var tags = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["os"] = Short(RuntimeInformation.OSDescription),
                ["osPlatform"] = GetPlatform(),
                ["is64bit"] = Bool(Environment.Is64BitProcess),
                ["status"] = "ok"
            };

            if (RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.X86)
            {
                Add(tags, "SSE2", System.Runtime.Intrinsics.X86.Sse2.IsSupported);
                Add(tags, "SSE41", System.Runtime.Intrinsics.X86.Sse41.IsSupported);
                Add(tags, "SSE42", System.Runtime.Intrinsics.X86.Sse42.IsSupported);
                Add(tags, "POPCNT", System.Runtime.Intrinsics.X86.Popcnt.IsSupported);
                Add(tags, "AES", System.Runtime.Intrinsics.X86.Aes.IsSupported);
                Add(tags, "PCLMUL", System.Runtime.Intrinsics.X86.Pclmulqdq.IsSupported);
                Add(tags, "AVX", System.Runtime.Intrinsics.X86.Avx.IsSupported);
                Add(tags, "AVX2", System.Runtime.Intrinsics.X86.Avx2.IsSupported);
#if NET9_0_OR_GREATER
                Add(tags, "AVX512F", System.Runtime.Intrinsics.X86.Avx512F.IsSupported);
#endif
                Add(tags, "BMI1", System.Runtime.Intrinsics.X86.Bmi1.IsSupported);
                Add(tags, "BMI2", System.Runtime.Intrinsics.X86.Bmi2.IsSupported);
                Add(tags, "FMA", System.Runtime.Intrinsics.X86.Fma.IsSupported);
            }
            else if (RuntimeInformation.ProcessArchitecture is Architecture.Arm64)
            {
                Add(tags, "ArmBase", System.Runtime.Intrinsics.Arm.ArmBase.IsSupported);
                Add(tags, "AdvSimd", System.Runtime.Intrinsics.Arm.AdvSimd.IsSupported);
                Add(tags, "CRC32", System.Runtime.Intrinsics.Arm.Crc32.IsSupported);
#if NET9_0_OR_GREATER
                Add(tags, "AES", System.Runtime.Intrinsics.Arm.Aes.IsSupported);
                Add(tags, "SHA1", System.Runtime.Intrinsics.Arm.Sha1.IsSupported);
                Add(tags, "SHA256", System.Runtime.Intrinsics.Arm.Sha256.IsSupported);
                Add(tags, "DOTPROD", System.Runtime.Intrinsics.Arm.Dp.IsSupported);
                Add(tags, "AdvSimd64", System.Runtime.Intrinsics.Arm.AdvSimd.Arm64.IsSupported);
#endif
            }

            var gb = _factory.Gauge(id, name);

            foreach (var kv in tags)
            {
                gb.WithTag(kv.Key, kv.Value);
            }

            var g = gb.Build();

            g.SetValue(1); // info metric: constant 1

            return Task.FromResult<IMetric?>(g);
        }
        catch (OperationCanceledException)
        {
            var g = _factory.Gauge(id, name).WithTag("status", "cancelled").Build();
            g.SetValue(0);
            return Task.FromResult<IMetric?>(g);
        }
        catch (Exception ex)
        {
            var g = _factory.Gauge(id, name).WithTag("status", "error").WithTag("reason", Short(ex.Message)).Build();
            g.SetValue(0);
            return Task.FromResult<IMetric?>(g);
        }

        static void Add(IDictionary<string, string> tags, string key, bool supported)
        {
            tags[key] = Bool(supported);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static string Bool(bool b) => b ? "true" : "false";

        static string GetPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "macOS";
            }

            return "Unknown";
        }

        static string Short(string s) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 160 ? s : s[..160]);
    }
#pragma warning restore CA1031

    // ---- Explicit IMetricCollector helper methods ----

    /// <summary>
    /// Creates a summary metric builder result using the provided quantiles and optional tags.
    /// </summary>
    /// <param name="id">The unique metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="quantiles">
    /// The desired quantiles (e.g., <c>0.5</c>, <c>0.9</c>, <c>0.99</c>). If <see langword="null"/>,
    /// the default set <c>{ 0.5, 0.9, 0.99 }</c> is applied.
    /// </param>
    /// <param name="tags">Optional tags to attach to the metric.</param>
    /// <param name="resetOnGet">
    /// Indicates whether the summary should reset on read. This parameter is currently ignored by the implementation
    /// but is retained for interface symmetry and forward compatibility.
    /// </param>
    /// <returns>A constructed <see cref="ISummaryMetric"/> instance.</returns>
    /// <remarks>
    /// The method is a convenience wrapper around <see cref="IMetricFactory.Summary(string, string)"/> and applies the provided
    /// quantiles and tags before building the metric instance.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var summary = collector.CreateSummary(
    ///     id: "cpu.features.scan.duration",
    ///     name: "CPU Feature Scan Duration",
    ///     quantiles: new[] { 0.5, 0.95, 0.99 },
    ///     tags: new Dictionary<string, string> { ["unit"] = "ms" },
    ///     resetOnGet: false);
    /// ]]></code>
    /// </example>
    public ISummaryMetric CreateSummary(string id, string name, IEnumerable<double> quantiles, IReadOnlyDictionary<string, string>? tags, bool resetOnGet)
    {
        var q = quantiles?.ToArray() ?? new[] { 0.5, 0.9, 0.99 };
        var sb = _factory.Summary(id, name).WithQuantiles(q);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                sb.WithTag(kv.Key, kv.Value);
            }
        }

        return sb.Build();
    }

    /// <summary>
    /// Creates a bucket histogram metric using the supplied bucket upper bounds and optional tags.
    /// </summary>
    /// <param name="id">The unique metric identifier.</param>
    /// <param name="name">The human-readable metric name.</param>
    /// <param name="bucketUpperBounds">
    /// The (sorted, increasing) list of bucket upper bounds. If <see langword="null"/>, an empty bound set is used.
    /// </param>
    /// <param name="tags">Optional tags to attach to the metric.</param>
    /// <returns>A constructed <see cref="IBucketHistogramMetric"/> instance.</returns>
    /// <remarks>
    /// This is a convenience wrapper around <see cref="IMetricFactory.Histogram(string, string)"/> that applies bounds and tags
    /// prior to building the metric instance.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// var histogram = collector.CreateBucketHistogram(
    ///     id: "cpu.features.scan.size_bytes",
    ///     name: "CPU Feature Scan Payload Size",
    ///     bucketUpperBounds: new[] { 64d, 128d, 256d, 512d, 1024d },
    ///     tags: new Dictionary<string, string> { ["unit"] = "bytes" });
    /// ]]></code>
    /// </example>
    public IBucketHistogramMetric CreateBucketHistogram(string id, string name, IEnumerable<double> bucketUpperBounds, IReadOnlyDictionary<string, string>? tags)
    {
        var bounds = bucketUpperBounds?.ToArray() ?? Array.Empty<double>();
        var hb = _factory.Histogram(id, name).WithBounds(bounds);

        if (tags is not null)
        {
            foreach (var kv in tags)
            {
                hb.WithTag(kv.Key, kv.Value);
            }
        }

        return hb.Build();
    }
}
