// <copyright file="NetMetricServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NetMetric.DependencyInjection;

/// <summary>
/// Extension methods for registering NetMetric core services into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type wires up option validation, the time provider, the central registry, the metric factory, and
/// the <see cref="MetricManager"/> into the dependency injection (DI) container with sensible defaults.
/// </para>
/// <para><b>Registered services:</b></para>
/// <list type="bullet">
///   <item><description><see cref="IOptions{TOptions}"/> for <see cref="MetricOptions"/> with validation.</description></item>
///   <item><description><see cref="ITimeProvider"/> as a singleton (<see cref="UtcTimeProvider"/>).</description></item>
///   <item><description><see cref="MetricRegistry"/> as a singleton.</description></item>
///   <item><description><see cref="IMetricFactory"/> as a singleton (<see cref="DefaultMetricFactory"/>).</description></item>
///   <item><description><see cref="MetricManager"/> as a singleton, built from the above.</description></item>
/// </list>
/// <para>
/// The registrations use <c>TryAdd*</c> semantics so that user-provided implementations can override defaults
/// by placing their registrations <em>before</em> calling these extensions.
/// </para>
/// </remarks>
public static class NetMetricCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetMetric core services and optionally configures <see cref="MetricOptions"/> via a delegate.
    /// </summary>
    /// <param name="services">The DI service collection to register services into. Must not be <see langword="null"/>.</param>
    /// <param name="configure">
    /// Optional configuration delegate applied with
    /// <see cref="OptionsServiceCollectionExtensions.PostConfigure{TOptions}(IServiceCollection, System.Action{TOptions})"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Use this overload when you want to supply options in code. For configuration binding from
    /// <see cref="IConfiguration"/>, prefer the other overload.
    /// </para>
    /// <para>
    /// Options are validated at startup via an <see cref="IValidateOptions{TOptions}"/> implementation that checks
    /// sampling range, timeouts, parallelism bounds, and tag limits for logical consistency.
    /// </para>
    /// </remarks>
    /// <example>
    /// Minimal setup:
    /// <code language="csharp"><![CDATA[
    /// services.AddNetMetricCore(opts =>
    /// {
    ///     opts.SamplingRate = 1.0;
    ///     opts.MaxTagKeyLength = 64;
    ///     opts.EnableParallelCollectors = true;
    /// });
    /// ]]></code>
    /// Overriding defaults:
    /// <code language="csharp"><![CDATA[
    /// services.AddSingleton<ITimeProvider, MyTimeProvider>();
    /// services.AddSingleton<IMetricFactory, MyFactory>();
    /// services.AddNetMetricCore();
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricCore(
        this IServiceCollection services,
        Action<MetricOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MetricOptions>();
        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<MetricOptions>, MetricOptionsValidation>());

        services.TryAddSingleton<ITimeProvider, UtcTimeProvider>();
        services.TryAddSingleton<MetricRegistry>();

        // MetricManager depends on MetricRegistry, IOptionsMonitor<MetricOptions>, ITimeProvider, and IMetricFactory.
        services.TryAddSingleton<MetricManager>(sp =>
        {
            var registry = sp.GetRequiredService<MetricRegistry>();
            var optsMon = sp.GetRequiredService<IOptionsMonitor<MetricOptions>>();
            var clock = sp.GetRequiredService<ITimeProvider>();
            var factory = sp.GetRequiredService<IMetricFactory>();
            return new MetricManager(registry, optsMon, clock, factory);
        });

        services.TryAddSingleton<IMetricFactory, DefaultMetricFactory>();
        return services;
    }

    /// <summary>
    /// Adds NetMetric core services and binds <see cref="MetricOptions"/> from an <see cref="IConfiguration"/> section.
    /// </summary>
    /// <param name="services">The DI service collection to register services into. Must not be <see langword="null"/>.</param>
    /// <param name="section">
    /// The configuration section to bind to <see cref="MetricOptions"/>. Must not be <see langword="null"/>.
    /// Typically something like <c>configuration.GetSection("NetMetric")</c>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Marked with <see cref="RequiresDynamicCodeAttribute"/> and <see cref="RequiresUnreferencedCodeAttribute"/> because
    /// options binding may require dynamic code or unreferenced members in trimming/AOT scenarios.
    /// </para>
    /// <para>
    /// This overload binds options and then delegates to <see cref="AddNetMetricCore(IServiceCollection, System.Action{MetricOptions}?)"/>
    /// for service registration and validation setup.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> or <paramref name="section"/> is <see langword="null"/>.
    /// </exception>
    /// <example>
    /// Binding from configuration:
    /// <code language="csharp"><![CDATA[
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddNetMetricCore(builder.Configuration.GetSection("NetMetric"));
    /// ]]></code>
    /// </example>
    [RequiresDynamicCode("Binding configuration to MetricOptions may require dynamic code generation, especially when using trimming or AOT compilation.")]
    [RequiresUnreferencedCode("Binding configuration to MetricOptions may require unreferenced members that are trimmed away in AOT scenarios.")]
    public static IServiceCollection AddNetMetricCore(
        this IServiceCollection services,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(section);

        services.AddOptions<MetricOptions>().Bind(section);
        return services.AddNetMetricCore();
    }

    // Internal validator is not publicly visible; instantiated by DI.
    /// <summary>
    /// Validates <see cref="MetricOptions"/> for logical consistency and value ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The following checks are enforced:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>SamplingRate</c> ∈ [0.0, 1.0].</description></item>
    ///   <item><description><c>CollectorTimeoutMs</c> ≥ 0.</description></item>
    ///   <item><description>If set, <c>CollectorParallelism</c> &gt; 0.</description></item>
    ///   <item><description><c>MaxTagKeyLength</c> and <c>MaxTagValueLength</c> ≥ 0 (0 disables).</description></item>
    ///   <item><description>If set, <c>MaxTagsPerMetric</c> &gt; 0.</description></item>
    /// </list>
    /// Returns <see cref="ValidateOptionsResult.Fail(string)"/> if any rule is violated; otherwise
    /// <see cref="ValidateOptionsResult.Success"/>.
    /// </remarks>
    [SuppressMessage(
        "Performance", "CA1812:Avoid uninstantiated internal classes",
        Justification = "Instantiated by the DI container via ServiceDescriptor registration.")]
    private sealed class MetricOptionsValidation : IValidateOptions<MetricOptions>
    {
        /// <summary>
        /// Performs validation for a given named options instance.
        /// </summary>
        /// <param name="name">The name of the options instance (may be <see langword="null"/> for default).</param>
        /// <param name="options">The options to validate.</param>
        /// <returns>A <see cref="ValidateOptionsResult"/> describing validation success or failure.</returns>
        public ValidateOptionsResult Validate(string? name, MetricOptions options)
        {
            if (options is null)
                return ValidateOptionsResult.Fail("MetricOptions is null.");

            if (options.SamplingRate is < 0.0 or > 1.0)
                return ValidateOptionsResult.Fail("SamplingRate must be in [0.0, 1.0].");

            if (options.CollectorTimeoutMs < 0)
                return ValidateOptionsResult.Fail("CollectorTimeoutMs must be >= 0.");

            if (options.CollectorParallelism is { } p && p <= 0)
                return ValidateOptionsResult.Fail("CollectorParallelism must be > 0 when set.");

            if (options.MaxTagKeyLength < 0 || options.MaxTagValueLength < 0)
                return ValidateOptionsResult.Fail("Tag key/value limits must be >= 0 (0 disables).");

            if (options.MaxTagsPerMetric is { } mt && mt <= 0)
                return ValidateOptionsResult.Fail("MaxTagsPerMetric must be > 0 when set.");

            return ValidateOptionsResult.Success;
        }
    }
}
