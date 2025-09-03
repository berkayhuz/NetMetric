// <copyright file="ServiceBusOptionsValidator.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.Options;

namespace NetMetric.Azure.Options.Validation;

/// <summary>
/// Validates <see cref="ServiceBusOptions"/> to ensure that configuration
/// values for Azure Service Bus integration are consistent and actionable.
/// </summary>
/// <remarks>
/// <para>
/// This validator focuses on logical consistency checks required by the Service Bus
/// queue depth collector. Specifically, when one or more queues are configured,
/// a fully qualified namespace (FQNS) must also be provided so that the collector can
/// connect to the correct Service Bus namespace.
/// </para>
/// <para>
/// The validation performed here is intentionally lightweight and complements (but does not
/// replace) runtime connectivity checks performed by the adapters/collectors. It does not
/// attempt network calls or permissions validation.
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     If <see cref="ServiceBusOptions.Queues"/> is non-empty, then
///     <see cref="ServiceBusOptions.FullyQualifiedNamespace"/> must be a non-empty, non-whitespace string.
///     </description>
///   </item>
/// </list>
/// <para>
/// Typical fully qualified namespace format is
/// <c>mybus.servicebus.windows.net</c>. The validator does not enforce DNS syntax; it only
/// requires that a value is provided when queues are specified.
/// </para>
/// </remarks>
/// <example>
/// The validator is commonly registered via options in <c>Startup</c> / <c>Program</c>:
/// <code language="csharp"><![CDATA[
/// services.AddOptions<ServiceBusOptions>()
///         .Bind(configuration.GetSection("NetMetric:Azure:ServiceBus"))
///         .ValidateOptions<ServiceBusOptions, ServiceBusOptionsValidator>();
///
/// // Alternatively using Microsoft.Extensions.Options:
/// services.AddSingleton<IValidateOptions<ServiceBusOptions>, ServiceBusOptionsValidator>();
/// ]]></code>
/// With the following configuration, validation will succeed:
/// <code language="json"><![CDATA[
/// {
///   "NetMetric": {
///     "Azure": {
///       "ServiceBus": {
///         "FullyQualifiedNamespace": "mybus.servicebus.windows.net",
///         "Queues": [ "orders", "payments" ],
///         "MaxQueuesPerCollect": 4
///       }
///     }
///   }
/// }
/// ]]></code>
/// With the following configuration, validation will fail because <c>Queues</c> is set but
/// <c>FullyQualifiedNamespace</c> is missing:
/// <code language="json"><![CDATA[
/// {
///   "NetMetric": {
///     "Azure": {
///       "ServiceBus": {
///         "Queues": [ "orders" ]
///       }
///     }
///   }
/// }
/// ]]></code>
/// </example>
/// <seealso cref="ServiceBusOptions"/>
/// <seealso cref="ValidateOptionsResult"/>
/// <threadsafety>
/// This type is stateless and thread-safe. Instances may be reused across validations.
/// </threadsafety>
internal sealed class ServiceBusOptionsValidator : IValidateOptions<ServiceBusOptions>
{
    /// <summary>
    /// Validates the given <see cref="ServiceBusOptions"/> instance.
    /// </summary>
    /// <param name="name">The name of the options instance being validated (may be <c>null</c>).</param>
    /// <param name="o">The <see cref="ServiceBusOptions"/> instance to validate. Must not be <c>null</c>.</param>
    /// <returns>
    /// A <see cref="ValidateOptionsResult"/> indicating success when the options are valid,
    /// or failure with a descriptive error message when validation rules are violated.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Rules:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///   If <see cref="ServiceBusOptions.Queues"/> contains one or more items, then
    ///   <see cref="ServiceBusOptions.FullyQualifiedNamespace"/> must be provided.
    ///   </description></item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="o"/> is <c>null</c>.
    /// </exception>
    public ValidateOptionsResult Validate(string? name, ServiceBusOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);

        // If any queues are configured, FullyQualifiedNamespace must be specified.
        if (o.Queues is { Count: > 0 } && string.IsNullOrWhiteSpace(o.FullyQualifiedNamespace))
        {
            return ValidateOptionsResult.Fail(
                "FullyQualifiedNamespace must be provided when Queues are specified.");
        }

        return ValidateOptionsResult.Success;
    }
}
