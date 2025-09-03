// <copyright file="CollectorBuilderHelpers.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Process.Internals;

/// <summary>
/// Helper methods for building and configuring metric collectors, including tag application.
/// </summary>
internal static class CollectorBuilderHelpers
{
    /// <summary>
    /// Applies the specified tags to the metric builder.
    /// This method adds tags to the builder if they are provided.
    /// </summary>
    /// <typeparam name="T">The type of the metric being built (must implement <see cref="IMetric"/>).</typeparam>
    /// <param name="b">The instrument builder used to build the metric.</param>
    /// <param name="tags">The tags to apply to the metric. If null, no tags will be applied.</param>
    public static void ApplyTags<T>(IInstrumentBuilder<T> b, IReadOnlyDictionary<string, string>? tags)
        where T : class, IMetric
    {
        ArgumentNullException.ThrowIfNull(b);

        // If no tags are provided, return early
        if (tags is null)
        {
            return;
        }

        // Apply each tag to the builder
        foreach (var kv in tags)
        {
            b.WithTag(kv.Key, kv.Value);
        }
    }
}
