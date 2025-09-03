// <copyright file="InfluxWriteRequest.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Export.InfluxDB.Internal;

/// <summary>
/// Represents an immutable write request payload for the InfluxDB v2 <c>/api/v2/write</c> endpoint,
/// including organization, bucket, timestamp precision, and the line protocol body.
/// </summary>
/// <remarks>
/// <para>
/// This value type is a <see langword="readonly"/> <see langword="record struct"/>, making instances
/// compact to copy and safe to pass across layers (e.g., batching, retry pipelines) without defensive
/// copying. It contains no transport logic; callers are responsible for HTTP invocation and error handling.
/// </para>
/// <para>
/// <b>Transport expectations.</b> Typical clients POST the <see cref="Body"/> as the request content to
/// <c>/api/v2/write?org={Org}&amp;bucket={Bucket}&amp;precision={Precision}</c>, setting
/// <c>Content-Type: text/plain; charset=utf-8</c> and optionally <c>Authorization: Token &lt;token&gt;</c>.
/// The <see cref="Precision"/> value must match the timestamp units used in the line protocol body.
/// </para>
/// <para>
/// <b>Validation.</b> This type does not validate its inputs. Upstream code should ensure that:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Org"/> and <see cref="Bucket"/> are non-empty and URL-safe.</description></item>
///   <item><description><see cref="Precision"/> is one of <c>"s"</c>, <c>"ms"</c>, <c>"us"</c>, or <c>"ns"</c>.</description></item>
///   <item><description><see cref="Body"/> is a non-empty, well-formed Influx Line Protocol payload.</description></item>
/// </list>
/// <para>
/// <b>Thread-safety.</b> Instances are immutable and therefore thread-safe to share and cache.
/// </para>
/// </remarks>
/// <example>
/// The following example demonstrates constructing a request and sending it with <see cref="System.Net.Http.HttpClient"/>.
/// <code language="csharp"><![CDATA[
/// using System;
/// using System.Net.Http;
/// using System.Net.Http.Headers;
/// using System.Text;
/// using System.Threading;
/// using System.Threading.Tasks;
/// 
/// static async Task SendAsync(HttpClient http, InfluxWriteRequest req, string baseAddress, string token, CancellationToken ct)
/// {
///     // Build endpoint: /api/v2/write?org={Org}&bucket={Bucket}&precision={Precision}
///     var uri = $"{baseAddress.TrimEnd('/')}/api/v2/write?org={Uri.EscapeDataString(req.Org)}&bucket={Uri.EscapeDataString(req.Bucket)}&precision={Uri.EscapeDataString(req.Precision)}";
/// 
///     using var content = new StringContent(req.Body, Encoding.UTF8, "text/plain");
///     using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
/// 
///     // Optional: Token auth
///     if (!string.IsNullOrWhiteSpace(token))
///     {
///         request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
///     }
/// 
///     using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
///     response.EnsureSuccessStatusCode();
/// }
/// 
/// // Example construction:
/// var lineProtocol = "cpu,host=web-01,region=eu value=0.64 1725230400000000000\n";
/// var write = new InfluxWriteRequest(
///     Org: "netmetric",
///     Bucket: "metrics",
///     Precision: "ns",
///     Body: lineProtocol
/// );
/// ]]></code>
/// </example>
/// <param name="Org">
/// The target InfluxDB organization identifier used to resolve authorization and routing (query parameter <c>org</c>).
/// </param>
/// <param name="Bucket">
/// The destination bucket name into which metrics are written (query parameter <c>bucket</c>).
/// </param>
/// <param name="Precision">
/// The timestamp precision for the line protocol payload: one of <c>"s"</c> (seconds), <c>"ms"</c> (milliseconds),
/// <c>"us"</c> (microseconds), or <c>"ns"</c> (nanoseconds) (query parameter <c>precision</c>).
/// </param>
/// <param name="Body">
/// The Influx Line Protocol content to post in the request body. May contain one or more newline-separated points.
/// </param>
/// <seealso href="https://docs.influxdata.com/influxdb/v2/write-data/developer-tools/api/"/>
internal readonly record struct InfluxWriteRequest(string Org, string Bucket, string Precision, string Body);
