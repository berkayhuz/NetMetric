// <copyright file="MetricIntrospection.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Reflection;

namespace NetMetric.Export.JsonConsole.Internal;

/// <summary>
/// Provides helper methods for reading optional, convention-based metadata from metric instances
/// using reflection.
/// </summary>
/// <remarks>
/// <para>
/// Some metric implementations may expose additional public instance properties such as
/// <c>Unit</c>, <c>Description</c>, or <c>Kind</c>. This utility attempts to read those
/// properties dynamically at runtime without requiring a shared interface beyond the metric type itself.
/// Missing properties are treated as absent rather than being considered an error.
/// </para>
/// <para>
/// The reader uses <see cref="BindingFlags.Instance"/> and <see cref="BindingFlags.Public"/> and does
/// not cache <see cref="PropertyInfo"/> objects. If you call it in tight loops, consider adding a small
/// type-level cache at the call site to avoid repeated reflection lookups.
/// </para>
/// <para>
/// Trimming/AOT notes: the generic parameter is annotated with <c>DynamicallyAccessedMembers(PublicProperties)</c>
/// so that public properties are preserved when the application is trimmed. This helps keep the method
/// safe under IL trimming and ahead-of-time compilation scenarios.
/// </para>
/// </remarks>
internal static class MetricIntrospection
{
    /// <summary>
    /// Reads optional metadata from the given metric instance using reflection.
    /// </summary>
    /// <typeparam name="T">
    /// The metric type. Public instance properties are preserved for trimming via
    /// <c>DynamicallyAccessedMembers(PublicProperties)</c>. Must implement <see cref="IMetric"/>.
    /// </typeparam>
    /// <param name="m">The metric instance to inspect. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A tuple containing:
    /// <list type="bullet">
    ///   <item>
    ///     <description><c>Unit</c> - The display unit of the metric, if available; otherwise <see langword="null"/>.</description>
    ///   </item>
    ///   <item>
    ///     <description><c>Description</c> - A human-readable description of the metric, if available; otherwise <see langword="null"/>.</description>
    ///   </item>
    ///   <item>
    ///     <description><c>Kind</c> - The kind/category of the metric (for example, gauge or counter) if available; otherwise <see langword="null"/>.</description>
    ///   </item>
    /// </list>
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// The method performs non-throwing lookups: missing properties simply yield <see langword="null"/> values
    /// in the returned tuple. If a discovered property exists but its getter throws, that exception will
    /// propagate to the caller.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// IMetric metric = new CpuUtilizationMetric(); // Hypothetical metric implementation exposing Unit/Description/Kind
    /// var meta = MetricIntrospection.ReadMeta(metric);
    ///
    /// // Example usage when exporting:
    /// var unit = meta.Unit ?? "%";
    /// var description = meta.Description ?? "CPU utilization";
    /// var kind = meta.Kind ?? "gauge";
    /// ]]></code>
    /// </example>
    public static (string? Unit, string? Description, string? Kind)
        ReadMeta<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(T m)
        where T : IMetric
    {
        ArgumentNullException.ThrowIfNull(m);

        var t = typeof(T);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;

        string? unit = t.GetProperty("Unit", Flags)?.GetValue(m) as string;
        string? description = t.GetProperty("Description", Flags)?.GetValue(m) as string;
        var kindObj = t.GetProperty("Kind", Flags)?.GetValue(m);

        return (unit, description, kindObj?.ToString());
    }
}
