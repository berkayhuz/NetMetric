// © NetMetric 2025 - Apache-2.0
// <copyright file="StackdriverServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Google.Cloud.Monitoring.V3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.Export.Exporters;
using NetMetric.Export.Stackdriver.Exporters;
using NetMetric.Export.Stackdriver.Internals;

namespace NetMetric.Export.Stackdriver.DependencyInjection;

/// <summary>
/// Provides extension methods to register the NetMetric Stackdriver exporter
/// into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// These helpers wire up all required services to export NetMetric metrics to
/// Google Cloud Monitoring (Stackdriver). Registration follows a trimming/AOT-friendly
/// approach—no reflection-based configuration binding is used.
/// </para>
/// <para>
/// The registration:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Configures and validates <see cref="StackdriverExporterOptions"/> at startup.</description>
///   </item>
///   <item>
///     <description>Registers a singleton <see cref="Google.Cloud.Monitoring.V3.MetricServiceClient"/>.</description>
///   </item>
///   <item>
///     <description>Adds <see cref="StackdriverExporter"/> as an <see cref="IMetricExporter"/>.</description>
///   </item>
///   <item>
///     <description>
///       Post-configures <see cref="MetricOptions"/> to select an exporter automatically:
///       if exactly one exporter is registered, it is used; if multiple are present,
///       they are combined with <see cref="CompositeExporter"/>.
///     </description>
///   </item>
/// </list>
/// <para>
/// Validation performed at startup includes:
/// </para>
/// <list type="bullet">
///   <item><description><c>ProjectId</c> must be provided.</description></item>
///   <item><description><c>BatchSize</c> must be between 1 and 200 (inclusive).</description></item>
///   <item><description>Label length constraints must be greater than zero.</description></item>
///   <item>
///     <description>
///       When <see cref="StackdriverExporterOptions.ResourceType"/> is a known
///       Stackdriver monitored resource, required labels must be present (via
///       <see cref="ResourceValidator.IsResourceShapeValid"/>).
///     </description>
///   </item>
/// </list>
/// <para>
/// For production readiness, consider enabling descriptor creation via
/// <see cref="StackdriverExporterOptions.EnableCreateDescriptors"/> and ensure that
/// <see cref="StackdriverExporterOptions.ResourceType"/> and
/// <see cref="StackdriverExporterOptions.ResourceLabels"/> accurately describe your workload
/// (e.g., <c>gce_instance</c>, <c>k8s_pod</c>, etc.).
/// </para>
/// <seealso cref="StackdriverExporter"/>
/// <seealso cref="StackdriverExporterOptions"/>
/// <seealso cref="IMetricExporter"/>
/// <seealso cref="CompositeExporter"/>
/// </remarks>
public static class StackdriverServiceCollectionExtensions
{
    /// <summary>
    /// Registers the NetMetric Stackdriver exporter using a delegate to configure
    /// <see cref="StackdriverExporterOptions"/> in code.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">A delegate that configures <see cref="StackdriverExporterOptions"/>.</param>
    /// <returns>The provided <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This overload is trimming/AOT-safe and avoids reflection. Options are validated at startup,
    /// causing application startup to fail fast with a helpful message if configuration is invalid.
    /// </para>
    /// </remarks>
    /// <example>
    /// The following example configures the exporter in code:
    /// <code>
    /// services.AddNetMetricStackdriverExporter(o =>
    /// {
    ///     o.ProjectId = "my-gcp-project";
    ///     o.ResourceType = "gce_instance";
    ///     o.ResourceLabels["project_id"] = "my-gcp-project";
    ///     o.ResourceLabels["instance_id"] = "1234567890123456789";
    ///     o.ResourceLabels["zone"] = "us-central1-a";
    ///
    ///     o.MetricPrefix = "netmetric";
    ///     o.EnableCreateDescriptors = true;
    ///     o.BatchSize = 200; // up to 200 time series per request
    ///
    ///     o.Retry.MaxAttempts = 5;
    ///     o.Retry.InitialBackoffMs = 250;
    ///     o.Retry.MaxBackoffMs = 10_000;
    ///     o.Retry.Jitter = 0.2;
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddNetMetricStackdriverExporter(
        this IServiceCollection services,
        Action<StackdriverExporterOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<StackdriverExporterOptions>()
                .Configure(configure)
                .Validate(o => !string.IsNullOrWhiteSpace(o.ProjectId), "ProjectId required")
                .Validate(o => o.BatchSize is > 0 and <= 200, "BatchSize must be between 1 and 200")
                .Validate(o => o.MaxLabelKeyLength > 0 && o.MaxLabelValueLength > 0, "Label lengths must be > 0")
                .Validate(ResourceValidator.IsResourceShapeValid, "ResourceType requires specific labels")
                .ValidateOnStart();

        Register(services);
        return services;
    }

    /// <summary>
    /// Registers the NetMetric Stackdriver exporter using a configuration section.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="section">
    /// An <see cref="IConfiguration"/> section containing keys for
    /// <see cref="StackdriverExporterOptions"/>. Reflection-free mapping is performed.
    /// </param>
    /// <returns>The provided <paramref name="services"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="section"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This overload manually maps values from <paramref name="section"/> into
    /// <see cref="StackdriverExporterOptions"/> in a trimming/AOT-friendly manner.
    /// Collections (e.g., <see cref="StackdriverExporterOptions.ResourceLabels"/>) are
    /// read via <see cref="IConfiguration.GetSection(string)"/> and <c>.GetChildren()</c>.
    /// </para>
    /// <para>
    /// Options are validated at startup; invalid configuration prevents the application from starting.
    /// </para>
    /// </remarks>
    /// <example>
    /// Example <c>appsettings.json</c>:
    /// <code>
    /// {
    ///   "NetMetric": {
    ///     "Stackdriver": {
    ///       "ProjectId": "my-gcp-project",
    ///       "ResourceType": "gce_instance",
    ///       "ResourceLabels": {
    ///         "project_id": "my-gcp-project",
    ///         "instance_id": "1234567890123456789",
    ///         "zone": "us-central1-a"
    ///       },
    ///       "MetricPrefix": "netmetric",
    ///       "EnableCreateDescriptors": true,
    ///       "BatchSize": 200,
    ///       "MaxLabelKeyLength": 128,
    ///       "MaxLabelValueLength": 256,
    ///       "MaxLabelsPerMetric": 16,
    ///       "Retry": {
    ///         "MaxAttempts": 5,
    ///         "InitialBackoffMs": 250,
    ///         "MaxBackoffMs": 10000,
    ///         "Jitter": 0.2
    ///       },
    ///       "ProcessStart": "2025-01-01T00:00:00Z"
    ///     }
    ///   }
    /// }
    /// </code>
    /// <para>Registration in <c>Program.cs</c>:</para>
    /// <code>
    /// builder.Services.AddNetMetricStackdriverExporter(
    ///     builder.Configuration.GetSection("NetMetric:Stackdriver"));
    /// </code>
    /// </example>
    public static IServiceCollection AddNetMetricStackdriverExporter(
            this IServiceCollection services,
            IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);

        services.AddOptions<StackdriverExporterOptions>()
                .Configure(o => ConfigureFromSection(o, section))
                .Validate(o => !string.IsNullOrWhiteSpace(o.ProjectId), "ProjectId required")
                .Validate(o => o.BatchSize is > 0 and <= 200, "BatchSize must be between 1 and 200")
                .Validate(o => o.MaxLabelKeyLength > 0 && o.MaxLabelValueLength > 0, "Label lengths must be > 0")
                .Validate(ResourceValidator.IsResourceShapeValid, "ResourceType requires specific labels")
                .ValidateOnStart();

        Register(services);
        return services;
    }

    /// <summary>
    /// Registers the underlying services required by the Stackdriver exporter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// <para>
    /// Adds a singleton <see cref="Google.Cloud.Monitoring.V3.MetricServiceClient"/> and
    /// registers <see cref="StackdriverExporter"/> as an <see cref="IMetricExporter"/>.
    /// </para>
    /// </remarks>
    private static void Register(IServiceCollection services)
    {
        services.TryAddSingleton<MetricServiceClient>(_ => MetricServiceClient.Create());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IMetricExporter, StackdriverExporter>());
        WireExportersIntoOptions(services);
    }

    /// <summary>
    /// Post-configures <see cref="MetricOptions"/> to select an exporter automatically.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <remarks>
    /// <para>
    /// If no explicit <see cref="MetricOptions.Exporter"/> is provided:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>Exactly one exporter rarr; that exporter is used.</description></item>
    ///   <item><description>Multiple exporters rarr; a <see cref="CompositeExporter"/> is created.</description></item>
    ///   <item><description>No exporters rarr; option remains unassigned.</description></item>
    /// </list>
    /// </remarks>
    private static void WireExportersIntoOptions(IServiceCollection services)
    {
        services.AddOptions<MetricOptions>()
            .PostConfigure<IEnumerable<IMetricExporter>>((options, exporters) =>
            {
                ArgumentNullException.ThrowIfNull(options);
                if (options.Exporter is not null) return;

                var arr = exporters as IMetricExporter[] ?? exporters?.ToArray() ?? Array.Empty<IMetricExporter>();
                if (arr.Length == 0) return;

                options.Exporter = arr.Length == 1 ? arr[0] : new CompositeExporter(arr);
            });
    }

    /// <summary>
    /// Trimming/AOT-friendly manual mapping from <see cref="IConfiguration"/> into
    /// <see cref="StackdriverExporterOptions"/> (no reflection).
    /// </summary>
    /// <param name="o">The options instance to populate.</param>
    /// <param name="s">The configuration section to read from.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="o"/> or <paramref name="s"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Numeric and boolean values are parsed using invariant culture. Unknown keys are ignored.
    /// The <see cref="StackdriverExporterOptions.ResourceLabels"/> dictionary is populated from
    /// the <c>ResourceLabels</c> child section. The <c>Retry</c> sub-section maps to
    /// <see cref="RetryOptions"/>. <see cref="StackdriverExporterOptions.ProcessStart"/> is set
    /// if a valid <see cref="DateTimeOffset"/> is provided.
    /// </para>
    /// </remarks>
    private static void ConfigureFromSection(StackdriverExporterOptions o, IConfiguration s)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(s);

        // Helper: Invariant parse
        static bool TryParseBool(string? v, out bool result) => bool.TryParse(v, out result);
        static bool TryParseInt(string? v, out int result) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        static bool TryParseDouble(string? v, out double result) => double.TryParse(v, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);

        // Flat fields
        var v = s[nameof(StackdriverExporterOptions.ProjectId)];
        if (!string.IsNullOrWhiteSpace(v)) o.ProjectId = v;

        v = s[nameof(StackdriverExporterOptions.ResourceType)];
        if (!string.IsNullOrWhiteSpace(v)) o.ResourceType = v;

        v = s[nameof(StackdriverExporterOptions.MetricPrefix)];
        if (!string.IsNullOrWhiteSpace(v)) o.MetricPrefix = v;

        v = s[nameof(StackdriverExporterOptions.EnableCreateDescriptors)];
        if (TryParseBool(v, out var b)) o.EnableCreateDescriptors = b;

        v = s[nameof(StackdriverExporterOptions.BatchSize)];
        if (TryParseInt(v, out var i)) o.BatchSize = i;

        v = s[nameof(StackdriverExporterOptions.MaxLabelKeyLength)];
        if (TryParseInt(v, out i)) o.MaxLabelKeyLength = i;

        v = s[nameof(StackdriverExporterOptions.MaxLabelValueLength)];
        if (TryParseInt(v, out i)) o.MaxLabelValueLength = i;

        v = s[nameof(StackdriverExporterOptions.MaxLabelsPerMetric)];
        if (TryParseInt(v, out i)) o.MaxLabelsPerMetric = i;

        // Labels (trimming-safe; GetChildren AOT-friendly)
        var labelsSec = s.GetSection(nameof(StackdriverExporterOptions.ResourceLabels));
        foreach (var child in labelsSec.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key))
                o.ResourceLabels[child.Key] = child.Value ?? string.Empty;
        }

        // Retry
        var rsec = s.GetSection(nameof(StackdriverExporterOptions.Retry));
        if (rsec.Exists())
        {
            var rv = rsec[nameof(RetryOptions.MaxAttempts)];
            if (TryParseInt(rv, out i)) o.Retry.MaxAttempts = i;

            rv = rsec[nameof(RetryOptions.InitialBackoffMs)];
            if (TryParseInt(rv, out i)) o.Retry.InitialBackoffMs = i;

            rv = rsec[nameof(RetryOptions.MaxBackoffMs)];
            if (TryParseInt(rv, out i)) o.Retry.MaxBackoffMs = i;

            rv = rsec[nameof(RetryOptions.Jitter)];
            if (TryParseDouble(rv, out var d)) o.Retry.Jitter = d;
        }

        // ProcessStart (DateTimeOffset)
        var psStr = s[nameof(StackdriverExporterOptions.ProcessStart)];
        if (!string.IsNullOrWhiteSpace(psStr) &&
            DateTimeOffset.TryParse(psStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            o.ProcessStart = () => parsed;
        }
    }
}
