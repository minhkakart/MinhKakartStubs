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
    /// Automatically registers services marked with <see cref="ServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for services. When <c>null</c>, the current assembly is scanned.
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
    public static IServiceCollection RegisterServices(this IServiceCollection services, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var serviceDescriptors = AppUtils.GetClassTypes(assembly)
            .SelectMany(serviceClass =>
                serviceClass.GetCustomAttributes<ServiceAttribute>(true)
                    .SelectMany(serviceAttribute => CreateServiceDescriptors(serviceClass, serviceAttribute)))
            .ToImmutableList();

        foreach (var serviceDescriptor in serviceDescriptors)
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

        return services;

        IEnumerable<(ServiceDescriptor Descriptor, bool Multiple)> CreateServiceDescriptors(Type serviceClass, ServiceAttribute serviceAttribute)
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

                if (serviceType != serviceClass && !serviceType.IsAssignableFrom(serviceClass))
                {
                    throw new InvalidOperationException(
                        $"Cannot register {serviceClass.FullName} as {serviceType.FullName} because it does not implement the service type.");
                }

                yield return (new ServiceDescriptor(serviceType, serviceAttribute.Key, serviceClass, serviceAttribute.Lifetime), serviceAttribute.Multiple);
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
    public static IServiceCollection RegisterHostedServices(this IServiceCollection services, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var hostedServiceTypes = AppUtils.GetClassTypes(assembly)
            .Select(type => (HostType: type, Attribute: type.GetCustomAttribute<BackgroundServiceAttribute>()))
            .Where(item => item.Attribute is not null && typeof(IHostedService).IsAssignableFrom(item.HostType))
            .ToImmutableList();

        hostedServiceTypes.ForEach(item =>
        {
            var hostedServiceType = item.HostType;
            var backgroundServiceAttribute = item.Attribute!;
            var serviceType = backgroundServiceAttribute.ServiceType;

            if (serviceType is not null && serviceType != hostedServiceType && (!serviceType.IsInterface || !serviceType.IsAssignableFrom(hostedServiceType)))
            {
                throw new InvalidOperationException(
                    $"Cannot register hosted service {hostedServiceType.FullName} because BackgroundServiceAttribute.ServiceType must be an interface implemented by the hosted service or the hosted service type itself.");
            }

            services.AddSingleton<IHostedService>(sp =>
            {
                var singletonServiceAttribute = hostedServiceType.GetCustomAttributes<ServiceAttribute>(true)
                    .FirstOrDefault(attribute => attribute.Lifetime != ServiceLifetime.Singleton);
                if (singletonServiceAttribute is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot register hosted service {hostedServiceType.FullName} because it is marked with ServiceAttribute with lifetime {singletonServiceAttribute.Lifetime} instead of Singleton.");
                }

                if (serviceType is null)
                {
                    return (IHostedService)(sp.GetService(hostedServiceType) ?? ActivatorUtilities.CreateInstance(sp, hostedServiceType));
                }

                var service = sp.GetService(serviceType);
                if (service is null)
                {
                    throw new InvalidOperationException(
                        $"Cannot register hosted service {hostedServiceType.FullName} because service type {serviceType.FullName} is not registered. Register the service before calling RegisterHostedServices.");
                }

                if (service is not IHostedService hostedService)
                {
                    throw new InvalidOperationException(
                        $"Cannot register hosted service {hostedServiceType.FullName} because resolved service type {serviceType.FullName} does not implement {nameof(IHostedService)}.");
                }

                return hostedService;
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
    public static IServiceCollection RegisterHttpClients(this IServiceCollection services, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var httpClientTypes = AppUtils.GetClassTypes(assembly)
            .Where(t => t.GetCustomAttribute<HttpClientServiceAttribute>() is not null)
            .ToImmutableList();

        httpClientTypes.ForEach(httpClientType =>
        {
            var clientName = httpClientType.FullName ?? "default";
            var httpClientAttribute = httpClientType.GetCustomAttribute<HttpClientServiceAttribute>()!;
            Uri? baseUri = null;
            if (httpClientAttribute.BaseUrl is not null && !Uri.TryCreate(httpClientAttribute.BaseUrl, UriKind.Absolute, out baseUri))
            {
                throw new InvalidOperationException(
                    $"Cannot register HTTP client {httpClientType.FullName} because BaseUrl must be an absolute URI.");
            }

            foreach (var interceptorType in httpClientAttribute.Interceptors)
            {
                if (!typeof(DelegatingHandler).IsAssignableFrom(interceptorType))
                {
                    throw new InvalidOperationException(
                        $"Cannot register interceptor {interceptorType.FullName} because it does not inherit from {nameof(DelegatingHandler)}.");
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

                    client.Timeout = TimeSpan.FromSeconds(httpClientAttribute.TimeoutSeconds);
                })
                .ConfigureAdditionalHttpMessageHandlers((handlers, sp) =>
                {
                    foreach (var interceptorType in httpClientAttribute.Interceptors)
                    {
                        var interceptor = (DelegatingHandler)(sp.GetService(interceptorType) ?? ActivatorUtilities.CreateInstance(sp, interceptorType));
                        handlers.Add(interceptor);
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
    /// <remarks>
    /// <example>
    /// <code>
    /// services.RegisterOptions(configuration, typeof(MyOptions).Assembly);
    /// </code>
    /// </example>
    /// </remarks>
    public static IServiceCollection RegisterOptions(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var optionTypes = AppUtils.GetClassTypes(assembly)
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
}