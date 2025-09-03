// <copyright file="Severity.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

namespace NetMetric.Certificates.Abstractions;

/// <summary>
/// Provides severity level constants and helper methods for certificate status evaluation.
/// </summary>
/// <remarks>
/// <para>
/// This static class is used to categorize certificate validity based on the number
/// of days remaining until expiration. It maps certificate lifetime thresholds into
/// well-known severity strings (<see cref="Ok"/>, <see cref="Warn"/>, <see cref="Critical"/>, <see cref="Expired"/>).
/// </para>
/// <para>
/// Consumers typically use this class when aggregating certificate metadata into metrics
/// such as <c>nm.cert.severity_count</c> or when attaching severity tags to per-certificate samples.
/// </para>
/// </remarks>
/// <example>
/// Example usage in determining severity:
/// <code language="csharp"><![CDATA[
/// int warningThresholdDays = 30;
/// int criticalThresholdDays = 7;
///
/// double daysLeft = (certificate.NotAfterUtc - DateTime.UtcNow).TotalDays;
/// string severity = Severity.FromDaysLeft(daysLeft, warningThresholdDays, criticalThresholdDays);
///
/// Console.WriteLine($"Certificate subject={certificate.Subject}, severity={severity}");
/// // Possible outputs: "ok", "warn", "crit", "expired"
/// ]]></code>
/// </example>
public static class Severity
{
    /// <summary>
    /// Indicates that the certificate is valid and within safe lifetime limits.
    /// </summary>
    public const string Ok = "ok";

    /// <summary>
    /// Indicates that the certificate is approaching expiration (warning threshold).
    /// </summary>
    public const string Warn = "warn";

    /// <summary>
    /// Indicates that the certificate is critically close to expiration.
    /// </summary>
    public const string Critical = "crit";

    /// <summary>
    /// Indicates that the certificate has already expired.
    /// </summary>
    public const string Expired = "expired";

    /// <summary>
    /// Determines the severity level based on the number of days left until expiration.
    /// </summary>
    /// <param name="days">The number of days remaining until the certificate expires (negative if already expired).</param>
    /// <param name="warn">The threshold (in days) below which the certificate is considered <see cref="Warn"/>.</param>
    /// <param name="crit">The threshold (in days) below which the certificate is considered <see cref="Critical"/>.</param>
    /// <returns>
    /// One of the severity constants:
    /// <list type="bullet">
    /// <item><description><see cref="Expired"/> if <paramref name="days"/> is negative.</description></item>
    /// <item><description><see cref="Critical"/> if <paramref name="days"/> is less than or equal to <paramref name="crit"/>.</description></item>
    /// <item><description><see cref="Warn"/> if <paramref name="days"/> is less than or equal to <paramref name="warn"/>.</description></item>
    /// <item><description><see cref="Ok"/> otherwise.</description></item>
    /// </list>
    /// </returns>
    /// <example>
    /// Example decision logic:
    /// <code language="csharp"><![CDATA[
    /// string severity = Severity.FromDaysLeft(-2, warn: 30, crit: 7);
    /// // severity == "expired"
    ///
    /// severity = Severity.FromDaysLeft(5, warn: 30, crit: 7);
    /// // severity == "crit"
    ///
    /// severity = Severity.FromDaysLeft(20, warn: 30, crit: 7);
    /// // severity == "warn"
    ///
    /// severity = Severity.FromDaysLeft(90, warn: 30, crit: 7);
    /// // severity == "ok"
    /// ]]></code>
    /// </example>
    public static string FromDaysLeft(double days, int warn, int crit)
        => days < 0 ? Expired
        : days <= crit ? Critical
        : days <= warn ? Warn
        : Ok;
}
