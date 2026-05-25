namespace DiDecoration.Attributes;

/// <summary>
/// Specifies that a class should be registered as a hosted service.
/// </summary>
/// <remarks>
/// <para>
/// If <see cref="ServiceType"/> is <c>null</c>, the hosted class is registered directly as
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/>.
/// If <see cref="ServiceType"/> is specified, it must be either the hosted class itself or an interface implemented by that class.
/// </para>
/// <para>
/// When the hosted instance is resolved through another service registration, the implementation must exist by the time the container is built.
/// </para>
/// <example>
/// <code>
/// [BackgroundService]
/// public sealed class MyWorker : BackgroundService { }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BackgroundServiceAttribute(Type? serviceType = null) : Attribute
{
    /// <summary>
    /// Gets the optional service type used to resolve the hosted instance from dependency injection.
    /// </summary>
    public Type? ServiceType { get; init; } = serviceType;
}


