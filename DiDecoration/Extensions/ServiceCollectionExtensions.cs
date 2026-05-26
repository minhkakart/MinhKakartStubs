using System.Collections.Immutable;
using System.Reflection;
using DiDecoration.Attributes;
using DiDecoration.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DiDecoration.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all decoration-based components from the supplied assembly using a single call.
    /// When the separate source generator package is referenced, prefer the generated helpers in
    /// <c>DiDecoration.Generated.DecorationRegistrationExtensions</c> for reflection-free startup;
    /// this method remains the runtime-scanning fallback.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Optional configuration root used for option binding.</param>
    /// <param name="assembly">The assembly to scan. When <c>null</c>, the current assembly is scanned.</param>
    /// <param name="scanOptions">Optional scan filters that apply to all registrations.</param>
    public static IServiceCollection RegisterDecorators(this IServiceCollection services, IConfiguration? configuration = null, Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RegisterServices(assembly, scanOptions);
        services.RegisterHostedServices(assembly, scanOptions);
        services.RegisterHttpClients(assembly, scanOptions);

        if (configuration is not null)
        {
            services.RegisterOptions(configuration, assembly, scanOptions);
        }

        return services;
    }

    /// <summary>
    /// Automatically registers services marked with <see cref="ServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for services. When <c>null</c>, the current assembly is scanned.
    /// </param>
    /// <param name="scanOptions">
    /// Optional scan filters that narrow which classes are included.
    /// </param>
    /// <remarks>
    /// <para>
    /// Classes can declare multiple <see cref="ServiceAttribute"/> instances. By default, the first registration for a service type wins;
    /// set <see cref="ServiceAttribute.Multiple"/> to <c>true</c> when you want to keep additional registrations in the collection.
    /// </para>
    /// <para>
    /// <example>
    /// <code>
    /// services.RegisterServices(typeof(MyService).Assembly);
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    public static IServiceCollection RegisterServices(this IServiceCollection services, Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var serviceDescriptors = AppUtils.GetClassTypes(assembly, scanOptions)
            .SelectMany(serviceClass =>
                serviceClass.GetCustomAttributes<ServiceAttribute>(true)
                    .SelectMany(serviceAttribute => CreateServiceDescriptors(serviceClass, serviceAttribute)))
            .ToImmutableList();

        foreach (var serviceDescriptor in serviceDescriptors)
        {
            if (serviceDescriptor.Key is not null)
            {
                if (!serviceDescriptor.Multiple && services.Any(descriptor => descriptor.ServiceType == serviceDescriptor.Descriptor.ServiceType && Equals(descriptor.ServiceKey, serviceDescriptor.Key)))
                {
                    continue;
                }

                services.Add(serviceDescriptor.Descriptor);
            }
            else
            {
                if (serviceDescriptor.Multiple)
                {
                    services.Add(serviceDescriptor.Descriptor);
                }
                else
                {
                    services.TryAdd(serviceDescriptor.Descriptor);
                }
            }
        }

        return services;

        IEnumerable<(ServiceDescriptor Descriptor, bool Multiple, object? Key)> CreateServiceDescriptors(Type serviceClass, ServiceAttribute serviceAttribute)
        {
            var serviceTypes = serviceAttribute.ServiceTypes is { Length: > 0 }
                ? serviceAttribute.ServiceTypes
                : [serviceClass];

            foreach (var serviceType in serviceTypes)
            {
                if (serviceType is null)
                {
                    throw new InvalidOperationException($"Service class {serviceClass.FullName} contains a null service type in ServiceAttribute.");
                }

                if (!IsValidServiceMapping(serviceClass, serviceType))
                {
                    throw new InvalidOperationException(
                        $"Cannot register {serviceClass.FullName} as {serviceType.FullName} because it does not implement the service type.");
                }

                yield return (CreateServiceDescriptor(serviceType, serviceClass, serviceAttribute.Lifetime, serviceAttribute.Key), serviceAttribute.Multiple, serviceAttribute.Key);
            }
        }
    }

    /// <summary>
    /// Automatically registers hosted services marked with <see cref="BackgroundServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add hosted services to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for hosted services. When <c>null</c>, the current assembly is scanned.
    /// </param>
    /// <param name="scanOptions">
    /// Optional scan filters that narrow which classes are included.
    /// </param>
    /// <remarks>
    /// <para>
    /// If <see cref="BackgroundServiceAttribute.ServiceType"/> is <c>null</c>, the hosted class is registered directly as <see cref="IHostedService"/>.
    /// If <see cref="BackgroundServiceAttribute.ServiceType"/> is specified, it must be either the hosted class itself or an interface implemented
    /// by that class.
    /// </para>
    /// <para>
    /// When a hosted service is resolved through another service type, the service must exist by the time the container is built.
    /// Because resolution happens from the final service provider, the registration order of the two calls is flexible.
    /// </para>
    ///
    /// <example>
    /// <code>
    /// services.RegisterServices(typeof(MyWorker).Assembly)
    ///         .RegisterHostedServices(typeof(MyWorker).Assembly);
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection RegisterHostedServices(this IServiceCollection services, Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var hostedServiceTypes = AppUtils.GetClassTypes(assembly, scanOptions)
            .Select(type => (HostType: type, Attribute: type.GetCustomAttribute<BackgroundServiceAttribute>()))
            .Where(item => item.Attribute is not null && typeof(IHostedService).IsAssignableFrom(item.HostType))
            .ToImmutableList();

        hostedServiceTypes.ForEach(item =>
        {
            var hostedServiceType = item.HostType;
            var backgroundServiceAttribute = item.Attribute!;
            var serviceType = backgroundServiceAttribute.ServiceType;
            var key = backgroundServiceAttribute.Key;

            var singletonServiceAttribute = hostedServiceType.GetCustomAttributes<ServiceAttribute>(true)
                .FirstOrDefault(attribute => attribute.Lifetime != ServiceLifetime.Singleton);
            if (singletonServiceAttribute is not null)
            {
                throw new InvalidOperationException(
                    $"Cannot register hosted service {hostedServiceType.FullName} because it is marked with ServiceAttribute with lifetime {singletonServiceAttribute.Lifetime} instead of Singleton.");
            }

            if (serviceType is not null && serviceType != hostedServiceType && (!serviceType.IsInterface || !serviceType.IsAssignableFrom(hostedServiceType)))
            {
                throw new InvalidOperationException(
                    $"Cannot register hosted service {hostedServiceType.FullName} because BackgroundServiceAttribute.ServiceType must be an interface implemented by the hosted service or the hosted service type itself.");
            }

            services.AddSingleton<IHostedService>(sp =>
            {
                return ResolveHostedService(sp, hostedServiceType, serviceType, key);
            });
        });

        return services;
    }

    /// <summary>
    /// Automatically registers typed HTTP clients marked with <see cref="HttpClientServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add HTTP clients to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for clients. When <c>null</c>, the current assembly is scanned.
    /// </param>
    /// <param name="scanOptions">
    /// Optional scan filters that narrow which classes are included.
    /// </param>
    /// <remarks>
    /// <para>
    /// Base URLs and handler types are validated before the client registration is added so configuration errors fail fast.
    /// </para>
    /// <example>
    /// <code>
    /// services.RegisterHttpClients(typeof(CatalogClient).Assembly);
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection RegisterHttpClients(this IServiceCollection services, Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var httpClientTypes = AppUtils.GetClassTypes(assembly, scanOptions)
            .Where(t => t.GetCustomAttribute<HttpClientServiceAttribute>() is not null)
            .ToImmutableList();

        httpClientTypes.ForEach(httpClientType =>
        {
            var httpClientAttribute = httpClientType.GetCustomAttribute<HttpClientServiceAttribute>()!;
            var clientName = httpClientAttribute.ClientName?.Trim();
            if (string.IsNullOrWhiteSpace(clientName))
            {
                clientName = httpClientType.FullName ?? "default";
            }

            Uri? baseUri = null;
            if (httpClientAttribute.BaseUrl is not null && !Uri.TryCreate(httpClientAttribute.BaseUrl, UriKind.Absolute, out baseUri))
            {
                throw new InvalidOperationException(
                    $"Cannot register HTTP client {httpClientType.FullName} because BaseUrl must be an absolute URI.");
            }

            var handlerTypes = (httpClientAttribute.Handlers is { Length: > 0 }
                ? httpClientAttribute.Handlers
                : httpClientAttribute.Interceptors).Cast<Type?>().ToList();

            foreach (var header in httpClientAttribute.DefaultHeaders)
            {
                ValidateHeaderDefinition(header, httpClientType);
            }

            foreach (var handlerType in handlerTypes)
            {
                if (handlerType is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot register HTTP client {httpClientType.FullName} because a handler type is null.");
                }

                if (!typeof(DelegatingHandler).IsAssignableFrom(handlerType))
                {
                    throw new InvalidOperationException(
                        $"Cannot register handler {handlerType.FullName} because it does not inherit from {nameof(DelegatingHandler)}.");
                }
            }

            if (services.Any(descriptor => descriptor.ServiceType == httpClientType))
            {
                return;
            }

            services.AddHttpClient(clientName, client =>
                {
                    if (baseUri is not null)
                    {
                        client.BaseAddress = baseUri;
                    }

                    ApplyDefaultHeaders(client, httpClientAttribute.DefaultHeaders, httpClientType);
                    client.Timeout = TimeSpan.FromSeconds(httpClientAttribute.TimeoutSeconds);
                })
                .ConfigureAdditionalHttpMessageHandlers((handlers, sp) =>
                {
                    foreach (var handlerType in handlerTypes)
                    {
                        if (handlerType is null)
                        {
                            throw new InvalidOperationException(
                                $"Cannot register HTTP client {httpClientType.FullName} because a handler type is null.");
                        }

                        var resolvedHandlerType = handlerType;
                        var handler = (DelegatingHandler)(sp.GetService(resolvedHandlerType) ?? ActivatorUtilities.CreateInstance(sp, resolvedHandlerType));
                        handlers.Add(handler);
                    }
                });

            services.TryAddTransient(httpClientType, sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(clientName);
                return ActivatorUtilities.CreateInstance(sp, httpClientType, httpClient);
            });
        });

        return services;
    }

    /// <summary>
    /// Automatically binds option classes marked with <see cref="OptionAttribute"/> from the supplied configuration.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add options to.
    /// </param>
    /// <param name="configuration">
    /// The <see cref="IConfiguration"/> root used to resolve each option section.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for options. When <c>null</c>, the current assembly is scanned.
    /// </param>
    /// <param name="scanOptions">
    /// Optional scan filters that narrow which classes are included.
    /// </param>
    /// <remarks>
    /// <example>
    /// <code>
    /// services.RegisterOptions(configuration, typeof(MyOptions).Assembly);
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection RegisterOptions(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var optionTypes = AppUtils.GetClassTypes(assembly, scanOptions)
            .Select(type => (Type: type, Attribute: type.GetCustomAttribute<OptionAttribute>(false)))
            .Where(item => item.Attribute is not null);

        foreach (var item in optionTypes)
        {
            var configSection = configuration.GetSection(item.Attribute!.Key);
            var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
                .GetMethods()
                .First(m => m.Name == "Configure" && m.GetParameters().Length == 2)
                .MakeGenericMethod(item.Type);

            configureMethod.Invoke(null, [services, configSection]);
        }

        return services;
    }

    private static bool IsValidServiceMapping(Type serviceClass, Type serviceType)
    {
        if (serviceType == serviceClass)
        {
            return true;
        }

        if (serviceClass.IsGenericTypeDefinition || serviceType.IsGenericTypeDefinition)
        {
            if (!serviceClass.IsGenericTypeDefinition || !serviceType.IsGenericTypeDefinition)
            {
                return false;
            }

            return serviceClass.GetInterfaces()
                .Any(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == serviceType);
        }

        return serviceType.IsAssignableFrom(serviceClass);
    }

    private static ServiceDescriptor CreateServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime, object? key)
        => key is null
            ? ServiceDescriptor.Describe(serviceType, implementationType, lifetime)
            : ServiceDescriptor.DescribeKeyed(serviceType, key, implementationType, lifetime);

    private static IHostedService ResolveHostedService(IServiceProvider sp, Type hostedServiceType, Type? serviceType, object? key)
    {
        if (serviceType is null)
        {
            if (key is null)
            {
                return (IHostedService)(sp.GetService(hostedServiceType) ?? ActivatorUtilities.CreateInstance(sp, hostedServiceType));
            }

            var keyedHostedService = sp.GetKeyedService(hostedServiceType, key);
            if (keyedHostedService is IHostedService keyedResolvedHostedService)
            {
                return keyedResolvedHostedService;
            }

            throw new InvalidOperationException(
                $"Cannot register hosted service {hostedServiceType.FullName} because keyed service '{DescribeKey(key)}' is not registered. Register the keyed service before calling RegisterHostedServices.");
        }

        var service = key is null
            ? sp.GetService(serviceType)
            : sp.GetKeyedService(serviceType, key);

        if (service is null)
        {
            var keyMessage = key is null ? string.Empty : $" with key '{DescribeKey(key)}'";
            throw new InvalidOperationException(
                $"Cannot register hosted service {hostedServiceType.FullName} because service type {serviceType.FullName}{keyMessage} is not registered. Register the service before calling RegisterHostedServices.");
        }

        if (service is not IHostedService serviceResolvedHostedService)
        {
            var keyMessage = key is null ? string.Empty : $" with key '{DescribeKey(key)}'";
            throw new InvalidOperationException(
                $"Cannot register hosted service {hostedServiceType.FullName} because resolved service type {serviceType.FullName}{keyMessage} does not implement {nameof(IHostedService)}.");
        }

        return serviceResolvedHostedService;
    }

    private static string DescribeKey(object? key)
        => key switch
        {
            null => "<null>",
            string stringKey => stringKey,
            _ => key.ToString() ?? key.GetType().Name
        };

    private static void ValidateHeaderDefinition(string header, Type httpClientType)
    {
        var separatorIndex = header.IndexOf('=');
        if (separatorIndex < 0)
        {
            separatorIndex = header.IndexOf(':');
        }

        if (separatorIndex <= 0 || separatorIndex == header.Length - 1)
        {
            throw new InvalidOperationException(
                $"Cannot register HTTP client {httpClientType.FullName} because default header '{header}' must use the format Name=Value.");
        }
    }

    private static void ApplyDefaultHeaders(HttpClient client, string[] defaultHeaders, Type httpClientType)
    {
        foreach (var header in defaultHeaders)
        {
            var separatorIndex = header.IndexOf('=');
            if (separatorIndex < 0)
            {
                separatorIndex = header.IndexOf(':');
            }

            var name = header[..separatorIndex].Trim();
            var value = header[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Cannot register HTTP client {httpClientType.FullName} because default header '{header}' must use the format Name=Value.");
            }

            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }
    }
}