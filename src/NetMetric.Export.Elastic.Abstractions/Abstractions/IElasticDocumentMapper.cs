// <copyright file="IElasticDocumentMapper.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System;
using System.Collections.Generic;
using NetMetric.Abstractions;

namespace NetMetric.Export.Elastic.Abstractions;

/// <summary>
/// Defines a contract for mapping a single <see cref="IMetric"/> instance into a pair of
/// Elasticsearch-compatible NDJSON lines suitable for the Bulk API.
/// </summary>
/// <remarks>
/// <para>
/// The mapper must produce exactly two NDJSON lines for each metric:
/// </para>
/// <list type="number">
///   <item>
///     <description><b>Action/metadata line:</b> An <c>{"index":{...}}</c> instruction containing the
///     target index (and optionally other routing metadata) understood by Elasticsearch's <c>_bulk</c> endpoint.</description>
///   </item>
///   <item>
///     <description><b>Document line:</b> The serialized metric payload representing the <see cref="IMetric"/> 
///     and its fields/tags, timestamped with the provided UTC time parameter.</description>
///   </item>
/// </list>
/// <para>
/// Implementations should be:
/// </para>
/// <list type="bullet">
///   <item><description><b>Deterministic:</b> Given the same inputs, produce byte-for-byte identical output.</description></item>
///   <item><description><b>Allocation-conscious:</b> Avoid unnecessary intermediate allocations when possible.</description></item>
///   <item><description><b>AOT/trim friendly:</b> Prefer <see cref="System.Text.Json.Utf8JsonWriter"/> or similar approaches that work under trimming.</description></item>
/// </list>
/// <para><b>Thread-safety:</b> Implementations may or may not be thread-safe.
/// If an implementation is not thread-safe, consumers must ensure external synchronization when using a shared instance.</para>
/// </remarks>
/// <example>
/// The following example shows how an exporter might call the mapper and append the two NDJSON lines
/// to a bulk payload sent to Elasticsearch:
/// <code language="csharp"><![CDATA[
/// IElasticDocumentMapper mapper = new ElasticDocumentMapper(/* options */);
///
/// // Example inputs:
/// IMetric metric = GetMetric();              // Provided by NetMetric
/// DateTime nowUtc = DateTime.UtcNow;         // Timestamp to apply
/// string indexName = "metrics-2025.09.02";   // Daily-rolled index
///
/// IReadOnlyList<string> lines = mapper.Map(metric, nowUtc, indexName);
///
/// // lines[0] = {"index":{"_index":"metrics-2025.09.02"}}
/// // lines[1] = {"@timestamp":"2025-09-02T14:20:00Z","name":"app.requests","value":123,...}
///
/// // Append to NDJSON payload (each line followed by '\n'):
/// var sb = new StringBuilder(capacity: 1024);
/// sb.AppendLine(lines[0]);
/// sb.AppendLine(lines[1]);
///
/// // The resulting payload can be POSTed to: https://your-elastic:9200/_bulk
/// ]]></code>
/// </example>
public interface IElasticDocumentMapper
{
    /// <summary>
    /// Maps the specified <paramref name="metric"/> into exactly two NDJSON lines for bulk indexing into
    /// the Elasticsearch index identified by <paramref name="indexName"/>, timestamped with <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="metric">The <see cref="IMetric"/> to serialize into an Elasticsearch document.</param>
    /// <param name="utcNow">The UTC timestamp to embed into the resulting document (typically <see cref="DateTime.UtcNow"/>).</param>
    /// <param name="indexName">The name of the target Elasticsearch index (for example, a daily-rolled index).</param>
    /// <returns>
    /// A read-only list of exactly two strings, where:
    /// <list type="number">
    ///   <item><description>The first string is the action/metadata line (the <c>index</c> instruction).</description></item>
    ///   <item><description>The second string is the serialized metric document line.</description></item>
    /// </list>
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned lines are NDJSON-ready and should be written to the bulk request body as-is, each followed by a newline character.
    /// </para>
    /// <para>
    /// Implementations may validate <paramref name="indexName"/> for basic formatting constraints accepted by Elasticsearch.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown if <paramref name="metric"/> is <see langword="null"/> or if <paramref name="indexName"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown if <paramref name="indexName"/> is empty or contains characters not permitted by Elasticsearch index naming rules.
    /// </exception>
    IReadOnlyList<string> Map(IMetric metric, DateTime utcNow, string indexName);
}
