// <copyright file="ServiceCollectionExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMetric.Abstractions;
using NetMetric.Certificates.Abstractions;
using NetMetric.Certificates.Infra;
using NetMetric.Certificates.Modules;

namespace NetMetric.Certificates.DependencyInjection;

/// <summary>
/// Provides extension methods for registering NetMetric certificate monitoring services
/// into an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// This package wires up the certificate sources (e.g., file system and X.509 stores) and the
/// <see cref="CertificatesModule"/>, which exposes a set of certificate-related collectors
/// (days-left, severity counts, days-left histogram) as well as self-metrics
/// (scan duration, error count, last scan time).
/// </para>
/// <para>
/// Registration is additive and idempotent with respect to the built-in defaults—
/// explicit registrations you perform prior to calling these extensions are preserved.
/// </para>
/// </remarks>
/// <example>
/// Register with defaults and enable the current user's <c>My</c> store as a source:
/// <code language="csharp"><![CDATA[
/// using Microsoft.Extensions.DependencyInjection;
/// using NetMetric.Certificates.DependencyInjection;
///
/// var services = new ServiceCollection();
/// services.AddNetMetricCore(); // wherever IMetricFactory and IModule hosting come from
///
/// services.AddNetMetricCertificates(opts =>
/// {
///     opts.UseDefaultSources = true;    // adds CurrentUser\My store by default
///     opts.WarningDays = 30;            // severity thresholds
///     opts.CriticalDays = 7;
/// });
/// ]]></code>
/// </example>
/// <example>
/// Add additional sources (e.g., LocalMachine\My) before or after calling the extension:
/// <code language="csharp"><![CDATA[
/// services.AddSingleton<ICertificateSource>(
///     new X509StoreCertificateSource(StoreName.My, StoreLocation.LocalMachine));
///
/// services.AddNetMetricCertificates(); // will keep the source you registered
/// ]]></code>
/// </example>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds NetMetric certificate monitoring services and related components to the service collection.
    /// </summary>
    /// <param name="services">The service collection to register with.</param>
    /// <param name="configure">
    /// An optional delegate to configure <see cref="CertificatesOptions"/> prior to registration.
    /// If omitted, default options are used.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    /// <remarks>
    /// <para>
    /// When <see cref="CertificatesOptions.UseDefaultSources"/> is enabled, the current user's
    /// <c>My</c> certificate store (<see cref="StoreName.My"/> in <see cref="StoreLocation.CurrentUser"/>)
    /// is registered as a default <see cref="ICertificateSource"/>.
    /// </para>
    /// <para>
    /// This method also registers the <see cref="CertificatesModule"/> as an <see cref="IModule"/>.
    /// The module consumes configured certificate sources, options, and the <see cref="IMetricFactory"/>
    /// via dependency injection.
    /// </para>
    /// <para>
    /// The registration uses <see cref = "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)" />
    /// and<see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.DependencyInjection.ServiceDescriptor)"/>.
    /// to avoid overriding existing user registrations.
    /// </para>
    /// </remarks>
    /// <example>
    /// Minimal registration using defaults:
    /// <code language="csharp"><![CDATA[
    /// services.AddNetMetricCertificates();
    /// ]]></code>
    /// </example>
    /// <example>
    /// Customize options and add a file-based source:
    /// <code language="csharp"><![CDATA[
    /// services.AddSingleton<ICertificateSource>(
    ///     new FileCertificateSource("/etc/ssl/site.pem", "/etc/ssl/intermediate.crt"));
    ///
    /// services.AddNetMetricCertificates(opts =>
    /// {
    ///     opts.UseDefaultSources = false;          // don't auto-add CurrentUser\My
    ///     opts.ScanTtl = TimeSpan.FromSeconds(15); // cache snapshot for 15 seconds
    ///     opts.WarningDays = 20;
    ///     opts.CriticalDays = 5;
    /// });
    /// ]]></code>
    /// </example>
    public static IServiceCollection AddNetMetricCertificates(
        this IServiceCollection services,
        Action<CertificatesOptions>? configure = null)
    {
        var opts = new CertificatesOptions();
        configure?.Invoke(opts);

        // Register options as a singleton so downstream components consume a stable instance.
        services.TryAddSingleton(opts);

        if (opts.UseDefaultSources)
        {
            // Default source: CurrentUser\My certificate store
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ICertificateSource>(
                _ => new X509StoreCertificateSource(StoreName.My, StoreLocation.CurrentUser)));
        }

        // Register the module that composes sources and metric factory to emit certificate metrics.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModule, CertificatesModule>(sp =>
        {
            var sources = sp.GetServices<ICertificateSource>();
            var options = sp.GetRequiredService<CertificatesOptions>();
            var factory = sp.GetRequiredService<IMetricFactory>();
            return new CertificatesModule(sources, options, factory);
        }));

        return services;
    }
}
