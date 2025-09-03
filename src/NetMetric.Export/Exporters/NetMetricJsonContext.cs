// <copyright file="NetMetricJsonContext.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Text.Json.Serialization;

namespace NetMetric.Export.Exporters;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> that registers the JSON
/// contract metadata used by NetMetric exporters.
/// </summary>
/// <remarks>
/// <para>
/// This context enables ahead-of-time (AOT) and trimming-friendly serialization by
/// having the <see cref="JsonSerializer"/> generate serializers at compile time for
/// the known types used by the exporters (e.g., <see cref="MetricPayload"/> and
/// selected <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> shapes).
/// Using source generation avoids runtime reflection and prevents members from being
/// inadvertently removed by the linker in trimmed deployments.
/// </para>
///
/// <para>
/// The <see cref="JsonSourceGenerationOptionsAttribute"/> controls global behavior for
/// the context. With <c>GenerationMode = JsonSourceGenerationMode.Default</c>, the
/// generated metadata supports serialization and deserialization for all types
/// annotated via <see cref="JsonSerializableAttribute"/> within this context.
/// </para>
///
/// <para>
/// To serialize using this context, pass the corresponding generated type info to
/// <see cref="JsonSerializer"/> APIs. For example, use
/// <see cref="NetMetricJsonContext.Default"/>.<see cref="NetMetricJsonContext.MetricPayload"/>
/// when working with <see cref="MetricPayload"/> instances.
/// </para>
/// </remarks>
/// <example>
/// <code language="csharp"><![CDATA[
/// using System.Text.Json;
/// using NetMetric.Export.Exporters;
///
/// // Create a metric payload (example only).
/// var metric = new MetricPayload(/* constructor args if any */);
///
/// // Serialize with the source-generated context (AOT / trimming friendly).
/// string json = JsonSerializer.Serialize(metric, NetMetricJsonContext.Default.MetricPayload);
///
/// // Deserialize back to MetricPayload using the same context.
/// var roundtrip = JsonSerializer.Deserialize(json, NetMetricJsonContext.Default.MetricPayload);
///
/// // Serialize a tag bag (Dictionary<string, string>) using registered metadata.
/// var tags = new Dictionary<string, string> { ["host"] = "web-01", ["region"] = "eu-central" };
/// string tagsJson = JsonSerializer.Serialize(tags, NetMetricJsonContext.Default.DictionaryStringString);
/// ]]></code>
/// </example>
/// <example>
/// <para>Serializing an arbitrary value bag (<c>Dictionary&lt;string, object?&gt;</c>):</para>
/// <code language="csharp"><![CDATA[
/// var bag = new Dictionary<string, object?>
/// {
///     ["count"] = 42,
///     ["elapsedMs"] = 12.7,
///     ["ok"] = true,
///     ["note"] = "warm path"
/// };
///
/// string bagJson = JsonSerializer.Serialize(
///     bag,
///     NetMetricJsonContext.Default.DictionaryStringObjectNullable);
/// ]]></code>
/// </example>
/// <seealso cref="JsonSerializer"/>
/// <seealso cref="JsonSerializerContext"/>
/// <seealso cref="JsonSourceGenerationOptionsAttribute"/>
/// <seealso cref="JsonSerializableAttribute"/>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MetricPayload))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class NetMetricJsonContext : JsonSerializerContext { }
