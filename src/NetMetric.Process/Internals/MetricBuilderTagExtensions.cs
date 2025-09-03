// <copyright file="MetricBuilderTagExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using NetMetric.Process.Abstractions;
using NetMetric.Process.Configuration;

namespace NetMetric.Process.Internals;

/// <summary>
/// Extensions for applying default tags to metrics built using <see cref="IInstrumentBuilder{TMetric}"/>.
/// </summary>
internal static class MetricBuilderTagExtensions
{
    /// <summary>
    /// Applies default process-related tags to a metric builder. This includes:
    /// <list type="bullet">
    /// <item><description>Process ID (pid)</description></item>
    /// <item><description>Process name</description></item>
    /// <item><description>Machine name (if enabled in options)</description></item>
    /// </list>
    /// </summary>
    /// <typeparam name="TMetric">The type of metric being built (must implement <see cref="IMetric"/>).</typeparam>
    /// <param name="builder">The instrument builder used to build the metric.</param>
    /// <param name="proc">The process information provider, used to fetch the process ID and name.</param>
    /// <param name="opts">The options that determine whether to include default tags.</param>
    /// <returns>The builder with the default tags applied.</returns>
    public static IInstrumentBuilder<TMetric> WithProcessDefaultTags<TMetric>(
        this IInstrumentBuilder<TMetric> builder,
        IProcessInfoProvider proc,
        ProcessOptions opts)
        where TMetric : class, IMetric
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(proc);
        ArgumentNullException.ThrowIfNull(builder);

        // If default tags are not enabled, return the builder without any tags
        if (!opts.EnableDefaultTags)
        {
            return builder;
        }

        // Fetch process information and apply tags
        var pid = proc.Current?.Id ?? 0;
        var pname = opts.ProcessNameOverride ?? proc.Current?.ProcessName ?? "unknown";

        builder.WithTag("process.pid", pid.ToString());
        builder.WithTag("process.name", pname);

        // Optionally include the machine name in the tags
        if (opts.IncludeMachineNameTag)
        {
            try
            {
                builder.WithTag("machine", Environment.MachineName);
            }
            catch
            {
                throw;
            }
        }

        return builder;
    }
}
