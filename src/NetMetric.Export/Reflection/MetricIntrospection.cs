// <copyright file="MetricIntrospection.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Reflection;

namespace NetMetric.Export.Reflection;

/// <summary>
/// Provides reflection-based helpers for reading common metadata from metric instances.
/// </summary>
/// <remarks>
/// Some metric implementations expose conventional public properties such as <c>Unit</c>,
/// <c>Description</c>, and <c>Kind</c>. These helpers inspect the runtime (or generic)
/// type to retrieve those values in a safe, non-throwing manner when members are present.
///
/// Both overloads favor compatibility with trimming and ahead-of-time (AOT) compilation:
/// - The non-generic overload is annotated with <see cref="System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"/>
///   because it uses open-ended reflection over public properties at runtime.
/// - The generic overload constrains <c>TMetric</c> and uses
///   <see cref="System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute"/>
///   to help ensure public properties are preserved when the app is trimmed.
///
/// This type is stateless and thread-safe. Multiple callers can invoke the methods concurrently.
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// IMetric metric = GetCurrentRequestLatencyMetric();
/// var (unit, description, kind) = MetricIntrospection.ReadMeta(metric);
/// Console.WriteLine(unit);
/// ]]></code>
/// </example>
internal static class MetricIntrospection
{
    /// <summary>
    /// Reads metadata (<c>Unit</c>, <c>Description</c>, <c>Kind</c>) from the given metric instance
    /// using reflection over its public instance properties.
    /// </summary>
    /// <param name="m">The metric instance to inspect. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A tuple containing the values of <c>Unit</c>, <c>Description</c>, and <c>Kind</c>,
    /// or <see langword="null"/> values when those properties are missing.
    /// </returns>
    /// <remarks>
    /// This method uses open-ended reflection and may cause members to be trimmed away in
    /// size-optimized builds. If your application uses IL trimming or AOT compilation, prefer
    /// <see cref="ReadMeta{TMetric}(TMetric)"/> for stronger guarantees.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    [RequiresUnreferencedCode(
        "Reads public properties via reflection. Ensure involved metric types preserve public properties when trimming.")]
    internal static (string? Unit, string? Description, string? Kind) ReadMeta(IMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);

        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public;

        var t = m.GetType();

        string? unit = t.GetProperty("Unit", BF)?.GetValue(m) as string;
        string? description = t.GetProperty("Description", BF)?.GetValue(m) as string;
        string? kind = t.GetProperty("Kind", BF)?.GetValue(m)?.ToString();

        return (unit, description, kind);
    }

    /// <summary>
    /// Reads metadata (<c>Unit</c>, <c>Description</c>, <c>Kind</c>) from the given metric instance,
    /// using the static type <typeparamref name="TMetric"/> for improved trimming/AOT compatibility.
    /// </summary>
    /// <typeparam name="TMetric">
    /// The compile-time metric type, which must implement <c>IMetric</c>.
    /// </typeparam>
    /// <param name="m">The metric instance to inspect. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A tuple containing the values of the conventional <c>Unit</c>, <c>Description</c>, and <c>Kind</c> properties
    /// when present on <typeparamref name="TMetric"/>; otherwise <see langword="null"/> entries.
    /// </returns>
    /// <remarks>
    /// This overload avoids fully dynamic reflection against the runtime type by inspecting
    /// <typeparamref name="TMetric"/> directly. When used in trimmed builds, the
    /// <see cref="System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties"/>
    /// requirement helps ensure those properties are retained.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="m"/> is <see langword="null"/>.</exception>
    internal static (string? Unit, string? Description, string? Kind) ReadMeta
        <[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TMetric>
        (TMetric m) where TMetric : IMetric
    {
        ArgumentNullException.ThrowIfNull(m);

        const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public;

        var t = typeof(TMetric);

        string? unit = t.GetProperty("Unit", BF)?.GetValue(m) as string;
        string? description = t.GetProperty("Description", BF)?.GetValue(m) as string;
        string? kind = t.GetProperty("Kind", BF)?.GetValue(m)?.ToString();

        return (unit, description, kind);
    }
}
