using DiDecoration.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DiDecoration.Attributes;

/// <summary>
/// Indicates that a class is a service to be registered in the dependency injection container.
/// </summary>
/// <remarks>
/// This attribute can be applied to classes to specify their service lifetime and optionally the service types they implement. The <see cref="ServiceCollectionExtensions.RegisterServices"/> method will scan for classes with this attribute and register them accordingly.
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
    public bool Multiple { get; init; }

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

