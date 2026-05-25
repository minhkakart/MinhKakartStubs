using DiDecoration.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DiDecoration.Attributes;

/// <summary>
/// Indicates that a class is a service to be registered in the dependency injection container.
/// </summary>
/// <remarks>
/// This attribute can be applied to classes to specify their service lifetime and optionally the service types they implement. The <see cref="ServiceCollectionExtensions.AddServices"/> method will scan for classes with this attribute and register them accordingly.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public class ServiceAttribute : Attribute
{
    /// <summary>
    /// Gets the lifetime of the service. This determines how the service is instantiated and shared within the application.
    /// </summary>
    public ServiceLifetime Lifetime { get; init; }

    /// <summary>
    /// Gets an optional key that can be used to differentiate between multiple implementations of the same service type. This is useful when you have multiple services implementing the same interface and want to specify which one to inject.
    /// </summary>
    public object? Key { get; init; }

    /// <summary>
    /// Gets an optional array of service types that this class implements. If specified, the class will be registered as these service types in the dependency injection container. If not specified, the class will be registered as itself.
    /// </summary>
    public Type[]? ServiceTypes { get; init; }

    /// <summary>
    /// Gets a value indicating whether multiple implementations of the same service type are allowed. If true, the service will be registered even if another implementation of the same service type already exists in the container. If false, the service will only be registered if there is no existing implementation of the same service type.
    /// </summary>
    public bool Multiple { get;  init; }

    protected ServiceAttribute(ServiceLifetime lifetime, object? key = null, Type[]? serviceTypes = null, bool multiple = false)
    {
        Lifetime = lifetime;
        Key = key;
        ServiceTypes = serviceTypes;
        Multiple = multiple;

        foreach (var serviceType in serviceTypes ?? [])
        {
            if (serviceType is not null && !serviceType.IsInterface)
                throw new InvalidOperationException($"Service type {serviceType.FullName} must be an interface.");
        }
    }
}

/// <summary>
/// Specifies that a class is a transient service, which means a new instance will be created each time it is requested from the dependency injection container.
/// </summary>
public sealed class TransientServiceAttribute : ServiceAttribute
{
    public TransientServiceAttribute() : base(ServiceLifetime.Transient)
    {
    }

    public TransientServiceAttribute(params Type[] serviceType) : base(ServiceLifetime.Transient, null, serviceType)
    {
    }

    public TransientServiceAttribute(object key, Type serviceType) : base(ServiceLifetime.Transient, key, [serviceType])
    {
    }
}

/// <summary>
/// Specifies that a class is a singleton service, which means a single instance will be created and shared throughout the application's lifetime. All requests for this service will receive the same instance.
/// </summary>
public sealed class SingletonServiceAttribute : ServiceAttribute
{
    public SingletonServiceAttribute() : base(ServiceLifetime.Singleton)
    {
    }

    public SingletonServiceAttribute(params Type[] serviceType) : base(ServiceLifetime.Singleton, null, serviceType)
    {
    }

    public SingletonServiceAttribute(object key, Type serviceType) : base(ServiceLifetime.Singleton, key, [serviceType])
    {
    }
}

/// <summary>
/// Specifies that a class is a scoped service, which means a new instance will be created for each scope. In web applications, a scope typically corresponds to a single HTTP request, so each request will receive its own instance of the service. However, in other types of applications, you can create scopes manually to control the lifetime of scoped services.
/// </summary>
public sealed class ScopedServiceAttribute : ServiceAttribute
{
    public ScopedServiceAttribute() : base(ServiceLifetime.Scoped)
    {
    }

    public ScopedServiceAttribute(params Type[] serviceType) : base(ServiceLifetime.Scoped, null, serviceType)
    {
    }

    public ScopedServiceAttribute(object key, Type serviceType) : base(ServiceLifetime.Scoped, key, [serviceType])
    {
    }
}

/// <summary>
/// Specifies that a class is a hosted service, which means it will be registered as a singleton and will be started automatically when the application starts. Hosted services are typically used for background tasks that need to run continuously or on a schedule. The class must implement the <see cref="IHostedService"/> interface to be registered as a hosted service.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BackgroundServiceAttribute(Type? serviceType) : Attribute
{
    /// <summary>
    /// Gets an optional service type that this class implements. If specified, the class will be registered as this service type in the dependency injection container. If not specified, the class will be registered as itself.
    /// </summary>
    public Type? ServiceType { get; init; } = serviceType;
}

/// <summary>
/// Specifies that a class is an HTTP client service, which means it will be registered as a typed HTTP client in the dependency injection container. The class must have a constructor that accepts an <see cref="HttpClient"/> parameter to be registered as an HTTP client service. The <see cref="HttpClientServiceAttribute"/> allows you to specify a base URL for the HTTP client, a timeout for requests, and any interceptors that should be applied to the HTTP client. Interceptors can be used to add custom logic before or after sending HTTP requests, such as logging, retry policies, or authentication.
/// </summary>
/// <param name="baseUrl">
/// An optional base URL for the HTTP client. If specified, this URL will be used as the base address for all requests made by the HTTP client. If not specified, the base address will need to be set manually when configuring the HTTP client or when making requests.
/// </param>
/// <param name="timeoutSeconds">
/// An optional timeout in seconds for HTTP requests made by the client. If specified, this value will be used to set the timeout for the HTTP client. If not specified, a default timeout of 30 seconds will be used.
/// </param>
/// <param name="interceptors">
/// An optional array of interceptor types that should be applied to the HTTP client. Interceptors are classes that can implement custom logic to be executed before or after sending HTTP requests. This can include tasks such as logging request and response data, implementing retry policies, adding authentication headers, or modifying request parameters. Each interceptor type must implement the appropriate interface (e.g., <see cref="DelegatingHandler"/>) to be registered as an interceptor for the HTTP client.
/// </param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpClientServiceAttribute(string? baseUrl = null, int timeoutSeconds = 30, params Type[] interceptors) : Attribute
{
    public string? BaseUrl { get; } = baseUrl;
    public int TimeoutSeconds { get; } = timeoutSeconds;
    public Type[] Interceptors { get; } = interceptors;
}
