// <copyright file="DecorateExtensions.cs" company="NetMetric">
// Copyright (c) 2025 NetMetric.
// SPDX-License-Identifier: Apache-2.0
// Version: 1.0.0
// </copyright>

using Microsoft.Extensions.DependencyInjection;

namespace NetMetric.SignalR.Extensions;

/// <summary>
/// Provides helper extensions for decorating open generic service registrations contained in an
/// <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose</b><br/>
/// Enables wrapping existing service registrations with an open generic decorator (e.g.,
/// decorating <c>HubLifetimeManager&lt;THub&gt;</c> with <c>InstrumentedHubLifetimeManager&lt;THub&gt;</c>)
/// without changing the original registrations.
/// </para>
/// <para>
/// <b>Supported registrations</b><br/>
/// Works with descriptors created via <see cref="ServiceDescriptor.ImplementationType"/> (concrete type),
/// <see cref="ServiceDescriptor.ImplementationFactory"/> (factory), or
/// <see cref="ServiceDescriptor.ImplementationInstance"/> (singleton instance).
/// The original descriptor is replaced with a new factory that builds the decorator and
/// injects the original implementation instance.
/// </para>
/// <para>
/// <b>AOT / Trimming</b><br/>
/// This API uses reflection to close open generics and to activate types at runtime.
/// It is annotated with <see cref="RequiresUnreferencedCodeAttribute"/> and <see cref="RequiresDynamicCodeAttribute"/>
/// to convey potential limitations under trimming and NativeAOT. When targeting such environments,
/// prefer closed-generic registrations known at compile time or generator-based approaches.
/// </para>
/// <para>
/// <b>Thread-safety</b><br/>
/// The method mutates the provided <see cref="IServiceCollection"/> but does not share mutable state across calls.
/// It is safe to invoke during container configuration (single-threaded by convention).
/// </para>
/// <example>
/// The following example decorates all registered <c>HubLifetimeManager&lt;THub&gt;</c> implementations with
/// <c>InstrumentedHubLifetimeManager&lt;THub&gt;</c>:
/// <code language="csharp"><![CDATA[
/// using Microsoft.AspNetCore.SignalR;
/// using NetMetric.SignalR.Extensions;
/// using NetMetric.SignalR.Instrumentation;
///
/// var services = new ServiceCollection();
/// services.AddSignalR();
///
/// // Register your metrics implementation as usual
/// services.AddSingleton<ISignalRMetrics, DefaultSignalRMetrics>();
///
/// // Decorate all HubLifetimeManager<THub> with InstrumentedHubLifetimeManager<THub>
/// services.TryDecorate(typeof(HubLifetimeManager<>), typeof(InstrumentedHubLifetimeManager<>));
/// ]]></code>
/// </example>
/// </remarks>
public static class DecorateExtensions
{
    /// <summary>
    /// Attempts to decorate existing open generic service registrations with the specified open generic decorator type.
    /// </summary>
    /// <param name="services">The service collection to modify. Must not be <see langword="null"/>.</param>
    /// <param name="openServiceType">
    /// The open generic service type to decorate (e.g., <c>typeof(HubLifetimeManager&lt;&gt;)</c>).
    /// Must be an open generic type (i.e., <see cref="Type.IsGenericTypeDefinition"/> is <see langword="true"/>).
    /// </param>
    /// <param name="openDecoratorType">
    /// The open generic decorator type that wraps the original implementation
    /// (e.g., <c>typeof(InstrumentedHubLifetimeManager&lt;&gt;)</c>).
    /// Must be an open generic type and expose public constructors to be activatable.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance for fluent chaining.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Only arity-1 open generics are supported (e.g., <c>IFoo&lt;T&gt;</c> → <c>FooDecorator&lt;T&gt;</c>).
    /// The method iterates over <paramref name="services"/> and replaces matching descriptors with a
    /// factory that constructs <paramref name="openDecoratorType"/> closed over the original generic argument,
    /// passing the original implementation as the first constructor argument.
    /// </para>
    /// <para>
    /// <b>Activation semantics</b><br/>
    /// For <see cref="ServiceDescriptor.ImplementationType"/>, the inner implementation is created via
    /// <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/> using the current scope,
    /// preserving constructor injection. For <see cref="ServiceDescriptor.ImplementationFactory"/>, the original
    /// factory is invoked to produce the inner instance. For <see cref="ServiceDescriptor.ImplementationInstance"/>,
    /// the pre-constructed singleton instance is used as-is.
    /// </para>
    /// <para>
    /// <b>Decorator construction</b><br/>
    /// The decorator is activated via <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/>
    /// and receives the inner instance as the first parameter, followed by any other DI-resolvable arguments that
    /// appear in its constructor.
    /// </para>
    /// <para>
    /// <b>Lifetime preservation</b><br/>
    /// The original <see cref="ServiceDescriptor.ServiceType"/> and <see cref="ServiceDescriptor.Lifetime"/> are preserved.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/>, <paramref name="openServiceType"/>, or <paramref name="openDecoratorType"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="openServiceType"/> or <paramref name="openDecoratorType"/> is not an open generic type definition.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// Thrown when either open generic has an arity other than 1.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the decorator type cannot be closed over the service generic argument (e.g., due to generic constraints),
    /// or when the decorator cannot be constructed with the available services.
    /// </exception>
    /// <example>
    /// Decorating a custom repository:
    /// <code language="csharp"><![CDATA[
    /// services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
    /// services.TryDecorate(typeof(IRepository<>), typeof(CachingRepository<>));
    /// ]]></code>
    /// </example>
    [RequiresUnreferencedCode("Uses reflection to close generic types and activate types via constructors.")]
    [RequiresDynamicCode("Closes open generics and activates types at runtime; may not be available under NativeAOT.")]
    public static IServiceCollection TryDecorate(
        this IServiceCollection services,
        Type openServiceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type openDecoratorType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(openServiceType);
        ArgumentNullException.ThrowIfNull(openDecoratorType);

        if (!openServiceType.IsGenericTypeDefinition)
            throw new ArgumentException("openServiceType must be an open generic type (IsGenericTypeDefinition == true).", nameof(openServiceType));

        if (!openDecoratorType.IsGenericTypeDefinition)
            throw new ArgumentException("openDecoratorType must be an open generic type (IsGenericTypeDefinition == true).", nameof(openDecoratorType));

        // Currently support only arity-1 open generics (IService<T> -> Decorator<T>)
        if (openServiceType.GetGenericArguments().Length != 1 || openDecoratorType.GetGenericArguments().Length != 1)
        {
            throw new NotSupportedException("Only arity-1 open generics are supported (e.g., IFoo<T> → Decorator<T>).");
        }

        for (int i = 0; i < services.Count; i++)
        {
            var d = services[i];
            var svcType = d.ServiceType;

            if (!svcType.IsGenericType) continue;
            if (svcType.GetGenericTypeDefinition() != openServiceType) continue;

            var genArg = svcType.GetGenericArguments()[0];

            Type closedDecorator;
            try
            {
                closedDecorator = openDecoratorType.MakeGenericType(genArg);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to close decorator type '{openDecoratorType}' with generic argument '{genArg}'.", ex);
            }

            var prevFactory = d.ImplementationFactory;
            var prevType = d.ImplementationType;
            var prevInstance = d.ImplementationInstance;

            services[i] = ServiceDescriptor.Describe(
                svcType,
                sp =>
                {
                    // Resolve the original implementation instance
                    var inner = prevFactory != null
                        ? prevFactory(sp)
                        : prevType != null
                            ? ActivatorUtilities.CreateInstance(sp, prevType)
                            : prevInstance!; // instance-backed registration

                    // Create the decorator, injecting the original implementation
                    return ActivatorUtilities.CreateInstance(sp, closedDecorator, inner);
                },
                d.Lifetime);
        }

        return services;
    }
}
