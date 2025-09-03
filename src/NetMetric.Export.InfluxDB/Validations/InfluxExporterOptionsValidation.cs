// <copyright file="InfluxExporterOptionsValidation.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Export.InfluxDB.Validations;

/// <summary>
/// Validates configuration supplied for <see cref="InfluxExporterOptions"/> at application startup
/// and whenever the options are reloaded.
/// </summary>
/// <remarks>
/// <para>
/// This validator enforces the presence and basic correctness of required InfluxDB exporter
/// settings before any export operation begins. It is designed to be used with the
/// <see cref="IOptionsMonitor{TOptions}"/> or <see cref="IOptionsSnapshot{TOptions}"/> patterns so that
/// misconfiguration is surfaced early and clearly.
/// </para>
/// <para>
/// <b>What is validated</b> (summarized):
/// </para>
/// <list type="bullet">
///   <item>
///     <description><c>BaseAddress</c> must be a non-null, absolute <c>Uri</c>.</description>
///   </item>
///   <item>
///     <description><c>Org</c>, <c>Bucket</c>, and <c>Token</c> must be provided (non-empty).</description>
///   </item>
///   <item>
///     <description><c>Precision</c> must be one of <c>"ns"</c>, <c>"us"</c>, <c>"ms"</c>, or <c>"s"</c>.</description>
///   </item>
///   <item>
///     <description><c>MaxInFlight</c> must be greater than 0.</description>
///   </item>
///   <item>
///     <description><c>MaxRetries</c> must be greater than or equal to 0.</description>
///   </item>
///   <item>
///     <description><c>MinGzipSizeBytes</c> must be greater than or equal to 0.</description>
///   </item>
/// </list>
/// <para>
/// <b>Usage</b>: register this validator with the options system so that validation runs on startup
/// and when configuration is reloaded.
/// </para>
/// <example>
/// <code language="csharp"><![CDATA[
/// // app startup
/// services.AddOptions<InfluxExporterOptions>()
///         .Bind(Configuration.GetSection("Metrics:Influx"))
///         .ValidateOnStart(); // optional but recommended to fail fast
///
/// // Ensure the custom validator is discovered by DI
/// services.AddSingleton<IValidateOptions<InfluxExporterOptions>, InfluxExporterOptionsValidation>();
///
/// // Later, consume via IOptionsMonitor<InfluxExporterOptions> or IOptionsSnapshot<InfluxExporterOptions>
/// ]]></code>
/// </example>
/// </remarks>
internal sealed class InfluxExporterOptionsValidation : IValidateOptions<InfluxExporterOptions>
{
    /// <summary>
    /// Validates an <see cref="InfluxExporterOptions"/> instance and returns a
    /// <see cref="ValidateOptionsResult"/> indicating success or failure.
    /// </summary>
    /// <param name="name">
    /// The name of the options instance being validated, or <see langword="null"/> if not named.
    /// </param>
    /// <param name="o">The options instance to validate.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> describing the outcome of validation. If validation fails,
    /// the result contains a descriptive error message to aid troubleshooting.
    /// </returns>
    /// <remarks>
    /// This method never throws; it always returns a <see cref="ValidateOptionsResult"/>.
    /// </remarks>
    public ValidateOptionsResult Validate(string? name, InfluxExporterOptions o)
    {
        if (o is null)
        {
            return ValidateOptionsResult.Fail("Options is null.");
        }
        if (o.BaseAddress is null || !o.BaseAddress.IsAbsoluteUri)
        {
            return Fail("BaseAddress must be absolute Uri.");
        }
        if (string.IsNullOrWhiteSpace(o.Org))
        {
            return Fail("Org required.");
        }
        if (string.IsNullOrWhiteSpace(o.Bucket))
        {
            return Fail("Bucket required.");
        }
        if (string.IsNullOrWhiteSpace(o.Token))
        {
            return Fail("Token required.");
        }
        if (o.Precision is not ("ns" or "us" or "ms" or "s"))
        {
            return Fail("Precision must be ns/us/ms/s.");
        }
        if (o.MaxInFlight <= 0)
        {
            return Fail("MaxInFlight must be > 0.");
        }
        if (o.MaxRetries < 0)
        {
            return Fail("MaxRetries must be >= 0.");
        }
        if (o.MinGzipSizeBytes < 0)
        {
            return Fail("MinGzipSizeBytes must be >= 0.");
        }

        return ValidateOptionsResult.Success;

        static ValidateOptionsResult Fail(string m) => ValidateOptionsResult.Fail(m);
    }
}
