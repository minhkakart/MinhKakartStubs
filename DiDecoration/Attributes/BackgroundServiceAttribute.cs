namespace DiDecoration.Attributes;

/// <summary>
/// Specifies that a class should be registered as a hosted service.
/// </summary>
/// <remarks>
/// If <see cref="ServiceType"/> is <c>null</c>, the hosted class is registered directly as <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.
/// If <see cref="ServiceType"/> is specified, it must be either the hosted class itself or an interface implemented by that class.
/// In the interface case, the service must already be registered before calling
/// <see cref="DiDecoration.Extensions.ServiceCollectionExtensions.RegisterHostedServices(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Reflection.Assembly?)"/>
/// so the hosted service can be resolved from the container.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BackgroundServiceAttribute(Type? serviceType) : Attribute
{
    /// <summary>
    /// Gets the optional service type used to resolve the hosted instance from dependency injection.
    /// </summary>
    public Type? ServiceType { get; init; } = serviceType;
}


