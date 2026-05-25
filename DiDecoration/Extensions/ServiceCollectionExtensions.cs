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
    /// Automatically register services marked with <see cref="ServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add services to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for services.
    /// </param>
    public static IServiceCollection RegisterServices(this IServiceCollection services, Assembly? assembly = null)
    {
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
    /// Automatically register hosted services marked with <see cref="BackgroundServiceAttribute"/> in the specified assembly.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to add hosted services to.
    /// </param>
    /// <param name="assembly">
    /// The <see cref="Assembly"/> to scan for hosted services.
    /// </param>
    /// <remarks>
    /// If <see cref="BackgroundServiceAttribute.ServiceType"/> is <c>null</c>, the hosted class is registered directly as <see cref="IHostedService"/>.
    /// If <see cref="BackgroundServiceAttribute.ServiceType"/> is specified, it must be either the hosted class itself or an interface
    /// implemented by that class. In that case, the service must already be registered before calling this method so it can be resolved
    /// from the container.
    ///
    /// This method can be used together with <see cref="SingletonServiceAttribute"/>, but the registration order matters when
    /// <see cref="BackgroundServiceAttribute.ServiceType"/> points to another service registration.
    /// </remarks>
    public static IServiceCollection RegisterHostedServices(this IServiceCollection services, Assembly? assembly = null)
    {
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

    public static IServiceCollection RegisterHttpClients(this IServiceCollection services, Assembly? assembly = null)
    {
        var httpClientTypes = AppUtils.GetClassTypes(assembly)
            .Where(t => t.GetCustomAttribute<HttpClientServiceAttribute>() is not null)
            .ToImmutableList();
        httpClientTypes.ForEach(httpClientType =>
        {
            if (services.Any(descriptor => descriptor.ServiceType == httpClientType))
            {
                return;
            }

            var clientName = httpClientType.FullName ?? "default";
            var httpClientAttribute = httpClientType.GetCustomAttribute<HttpClientServiceAttribute>()!;
            services.AddHttpClient(clientName, client =>
                {
                    if (httpClientAttribute.BaseUrl is not null)
                    {
                        if (!Uri.TryCreate(httpClientAttribute.BaseUrl, UriKind.Absolute, out var baseUri))
                        {
                            throw new InvalidOperationException(
                                $"Cannot register HTTP client {httpClientType.FullName} because BaseUrl must be an absolute URI.");
                        }

                        client.BaseAddress = baseUri;
                    }

                    client.Timeout = TimeSpan.FromSeconds(httpClientAttribute.TimeoutSeconds);
                })
                .ConfigureAdditionalHttpMessageHandlers((handlers, sp) =>
                {
                    foreach (var interceptorType in httpClientAttribute.Interceptors)
                    {
                        if (!typeof(DelegatingHandler).IsAssignableFrom(interceptorType))
                        {
                            throw new InvalidOperationException(
                                $"Cannot register interceptor {interceptorType.FullName} because it does not inherit from {nameof(DelegatingHandler)}.");
                        }

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

    public static IServiceCollection RegisterOptions(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null)
    {
        var optionTypes = AppUtils.GetClassTypes(assembly)
            .Where(type => type.GetCustomAttributes(typeof(OptionAttribute), false).Length > 0);

        foreach (var type in optionTypes)
        {
            var attribute = (OptionAttribute)type.GetCustomAttributes(typeof(OptionAttribute), false).First();
            var configSection = configuration.GetSection(attribute.Key);
            var configureMethod = typeof(OptionsConfigurationServiceCollectionExtensions)
                .GetMethods()
                .First(m => m.Name == "Configure" && m.GetParameters().Length == 2)
                .MakeGenericMethod(type);

            configureMethod.Invoke(null, [services, configSection]);
        }

        return services;
    }
}