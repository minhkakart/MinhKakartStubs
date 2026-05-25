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
    public static IServiceCollection AddServices(this IServiceCollection services, Assembly? assembly = null)
    {
        var serviceClasses = AppUtils.GetClassTypes(assembly)
            .Where(t => t.GetCustomAttribute<ServiceAttribute>(true) is not null)
            .ToImmutableList();
        foreach (var serviceClass in serviceClasses)
        {
            var serviceAttribute = serviceClass.GetCustomAttribute<ServiceAttribute>(true)!;
            var serviceKey = serviceAttribute.Key;
            var serviceLifetime = serviceAttribute.Lifetime;
            var multiple = serviceAttribute.Multiple;

            var registerType = GetServiceType(serviceClass);

            if (multiple)
            {
                services.Add(new ServiceDescriptor(registerType, serviceKey, serviceClass, serviceLifetime));
            }
            else
            {
                services.TryAdd(new ServiceDescriptor(registerType, serviceKey, serviceClass, serviceLifetime));
            }
        }

        return services;

        Type GetServiceType(Type serviceClass)
        {
            var serviceAttribute = serviceClass.GetCustomAttribute<ServiceAttribute>(true)
                                   ?? throw new InvalidOperationException($"Service class {serviceClass.FullName} is missing the required ServiceAttribute.");
            var serviceLifetime = serviceAttribute.Lifetime;

            var declaredServiceType = serviceAttribute.ServiceType;
            if (declaredServiceType != null && !declaredServiceType.IsAssignableFrom(serviceClass))
            {
                throw new InvalidOperationException(
                    $"Cannot register {serviceClass.FullName} as {declaredServiceType.FullName} because it does not implement the service type.");
            }

            var markedInterfaceType = GetFirstDirectInterface(serviceClass);
            if (markedInterfaceType != null)
            {
                var interfaceAttribute = markedInterfaceType.GetCustomAttribute<ServiceAttribute>();
                if (interfaceAttribute == null || interfaceAttribute.Lifetime != serviceLifetime)
                {
                    markedInterfaceType = null;
                }
            }

            var registerType = declaredServiceType
                               ?? markedInterfaceType
                               ?? serviceClass;

            return registerType;
        }
    }

    private static Type? GetFirstDirectInterface(Type type)
    {
        var all = type.GetInterfaces();

        var fromBase = type.BaseType?.GetInterfaces() ?? Type.EmptyTypes;

        // Keep only interfaces introduced by this class so we don't accidentally register
        // parent abstractions when a derived type implements a more specific contract.
        var candidates = all.Except(fromBase).ToArray();

        var directMostSpecific = candidates
            .Where(i => !candidates.Any(other => other != i && i.IsAssignableFrom(other)))
            .ToArray();

        return directMostSpecific.FirstOrDefault();
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
    public static IServiceCollection AddHostedServices(this IServiceCollection services, Assembly? assembly = null)
    {
        var hostedServiceTypes = AppUtils.GetClassTypes(assembly)
            .Where(t => t.GetCustomAttribute<BackgroundServiceAttribute>() is not null && typeof(IHostedService).IsAssignableFrom(t))
            .ToImmutableList();

        hostedServiceTypes.ForEach(hostedServiceType =>
        {
            services.TryAddSingleton<IHostedService>(sp =>
            {
                var singletonServiceAttribute = hostedServiceType.GetCustomAttribute<ServiceAttribute>(true);
                if (singletonServiceAttribute is not null
                    && singletonServiceAttribute.Lifetime != ServiceLifetime.Singleton
                    && singletonServiceAttribute.ServiceType is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot register hosted service {hostedServiceType.FullName} because it is marked with ServiceAttribute with lifetime {singletonServiceAttribute.Lifetime} instead of Singleton.");
                }

                return (IHostedService)(sp.GetService(hostedServiceType) ?? ActivatorUtilities.CreateInstance(sp, hostedServiceType));
            });
        });

        return services;
    }

    public static IServiceCollection AddHttpClients(this IServiceCollection services, Assembly? assembly = null)
    {
        var httpClientTypes = AppUtils.GetClassTypes(assembly)
            .Where(t => t.GetCustomAttribute<HttpClientServiceAttribute>() is not null)
            .ToImmutableList();
        httpClientTypes.ForEach(httpClientType =>
        {
            var clientName = httpClientType.FullName ?? "default";
            var httpClientAttribute = httpClientType.GetCustomAttribute<HttpClientServiceAttribute>()!;
            services.AddHttpClient(clientName, client =>
                {
                    if (httpClientAttribute.BaseUrl != null) client.BaseAddress = new Uri(httpClientAttribute.BaseUrl);
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

    public static IServiceCollection AddOptions(this IServiceCollection services, IConfiguration configuration, Assembly? assembly = null)
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