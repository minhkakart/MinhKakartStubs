using System.Collections.Immutable;
using System.Reflection;
using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;

namespace DiDecoration.Utils;

/// <summary>
/// Indicates the severity of a decoration diagnostic.
/// </summary>
public enum DecorationDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Identifies the kind of decoration that produced a diagnostic.
/// </summary>
public enum DecorationDiagnosticKind
{
    Service,
    HostedService,
    HttpClient,
    Option
}

/// <summary>
/// Describes a single issue discovered while analyzing an assembly for decoration-based registrations.
/// </summary>
public sealed record DecorationDiagnostic(
    DecorationDiagnosticKind Kind,
    DecorationDiagnosticSeverity Severity,
    string TypeName,
    string Message);

/// <summary>
/// Aggregates diagnostics for attribute-driven registration across an assembly.
/// </summary>
public static class DecorationDiagnostics
{
    /// <summary>
    /// Scans an assembly and returns all discovered diagnostics without throwing.
    /// </summary>
    public static ImmutableList<DecorationDiagnostic> Analyze(Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        var diagnostics = ImmutableList.CreateBuilder<DecorationDiagnostic>();

        foreach (var type in AppUtils.GetClassTypes(assembly, scanOptions))
        {
            AnalyzeServices(type, diagnostics);
            AnalyzeHostedServices(type, diagnostics);
            AnalyzeHttpClients(type, diagnostics);
        }

        return diagnostics.ToImmutable();
    }

    /// <summary>
    /// Scans an assembly and throws when any error-level diagnostics are discovered.
    /// </summary>
    public static ImmutableList<DecorationDiagnostic> Validate(Assembly? assembly = null, DecorationScanOptions? scanOptions = null)
    {
        var diagnostics = Analyze(assembly, scanOptions);
        var errors = diagnostics.Where(d => d.Severity == DecorationDiagnosticSeverity.Error).ToImmutableList();

        if (errors.Count > 0)
        {
            throw new DecorationValidationException(errors);
        }

        return diagnostics;
    }

    private static void AnalyzeServices(Type type, ImmutableList<DecorationDiagnostic>.Builder diagnostics)
    {
        foreach (var attribute in type.GetCustomAttributes<ServiceAttribute>(true))
        {
            IEnumerable<Type?> serviceTypes = attribute.ServiceTypes is { Length: > 0 }
                ? attribute.ServiceTypes.Cast<Type?>()
                : [];

            foreach (var serviceType in serviceTypes)
            {
                if (serviceType is null)
                {
                    diagnostics.Add(new DecorationDiagnostic(
                        DecorationDiagnosticKind.Service,
                        DecorationDiagnosticSeverity.Error,
                        type.FullName ?? type.Name,
                        "ServiceAttribute contains a null service type."));
                    continue;
                }

                if (!IsValidServiceMapping(type, serviceType))
                {
                    diagnostics.Add(new DecorationDiagnostic(
                        DecorationDiagnosticKind.Service,
                        DecorationDiagnosticSeverity.Error,
                        type.FullName ?? type.Name,
                        $"Cannot register {type.FullName} as {serviceType.FullName} because it does not implement the service type."));
                }
            }
        }
    }

    private static void AnalyzeHostedServices(Type type, ImmutableList<DecorationDiagnostic>.Builder diagnostics)
    {
        var attribute = type.GetCustomAttribute<BackgroundServiceAttribute>();
        if (attribute is null)
        {
            return;
        }

        if (!typeof(IHostedService).IsAssignableFrom(type))
        {
            diagnostics.Add(new DecorationDiagnostic(
                DecorationDiagnosticKind.HostedService,
                DecorationDiagnosticSeverity.Error,
                type.FullName ?? type.Name,
                $"{type.FullName} is marked with BackgroundServiceAttribute but does not implement {nameof(IHostedService)}."));
        }

        if (attribute.ServiceType is not null && attribute.ServiceType != type && (!attribute.ServiceType.IsInterface || !attribute.ServiceType.IsAssignableFrom(type)))
        {
            diagnostics.Add(new DecorationDiagnostic(
                DecorationDiagnosticKind.HostedService,
                DecorationDiagnosticSeverity.Error,
                type.FullName ?? type.Name,
                "BackgroundServiceAttribute.ServiceType must be an interface implemented by the hosted service or the hosted service type itself."));
        }

        var nonSingletonServiceAttribute = type.GetCustomAttributes<ServiceAttribute>(true)
            .FirstOrDefault(serviceAttribute => serviceAttribute.Lifetime != Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton);

        if (nonSingletonServiceAttribute is not null)
        {
            diagnostics.Add(new DecorationDiagnostic(
                DecorationDiagnosticKind.HostedService,
                DecorationDiagnosticSeverity.Error,
                type.FullName ?? type.Name,
                $"ServiceAttribute lifetime {nonSingletonServiceAttribute.Lifetime} is not valid for hosted services; use Singleton."));
        }
    }

    private static void AnalyzeHttpClients(Type type, ImmutableList<DecorationDiagnostic>.Builder diagnostics)
    {
        var attribute = type.GetCustomAttribute<HttpClientServiceAttribute>();
        if (attribute is null)
        {
            return;
        }

        if (attribute.BaseUrl is not null && !Uri.TryCreate(attribute.BaseUrl, UriKind.Absolute, out _))
        {
            diagnostics.Add(new DecorationDiagnostic(
                DecorationDiagnosticKind.HttpClient,
                DecorationDiagnosticSeverity.Error,
                type.FullName ?? type.Name,
                "BaseUrl must be an absolute URI."));
        }

        if (!HasHttpClientConstructor(type))
        {
            diagnostics.Add(new DecorationDiagnostic(
                DecorationDiagnosticKind.HttpClient,
                DecorationDiagnosticSeverity.Error,
                type.FullName ?? type.Name,
                "The typed client must expose a constructor that accepts HttpClient."));
        }

        IEnumerable<Type?> handlerTypes = (attribute.Handlers is { Length: > 0 } ? attribute.Handlers : attribute.Interceptors).Cast<Type?>();
        foreach (var handlerType in handlerTypes)
        {
            if (handlerType is null)
            {
                diagnostics.Add(new DecorationDiagnostic(
                    DecorationDiagnosticKind.HttpClient,
                    DecorationDiagnosticSeverity.Error,
                    type.FullName ?? type.Name,
                    "A handler type is null."));
                continue;
            }

            if (!typeof(DelegatingHandler).IsAssignableFrom(handlerType))
            {
                diagnostics.Add(new DecorationDiagnostic(
                    DecorationDiagnosticKind.HttpClient,
                    DecorationDiagnosticSeverity.Error,
                    type.FullName ?? type.Name,
                    $"Handler type {handlerType.FullName} does not inherit from {nameof(DelegatingHandler)}."));
            }
        }

        foreach (var header in attribute.DefaultHeaders)
        {
            if (!IsValidHeaderDefinition(header))
            {
                diagnostics.Add(new DecorationDiagnostic(
                    DecorationDiagnosticKind.HttpClient,
                    DecorationDiagnosticSeverity.Error,
                    type.FullName ?? type.Name,
                    $"Default header '{header}' must use the format Name=Value."));
            }
        }
    }

    private static bool HasHttpClientConstructor(Type type)
        => type.GetConstructors().Any(ctor => ctor.GetParameters().Any(parameter => parameter.ParameterType == typeof(HttpClient)));

    private static bool IsValidHeaderDefinition(string header)
    {
        var separatorIndex = header.IndexOf('=');
        if (separatorIndex < 0)
        {
            separatorIndex = header.IndexOf(':');
        }

        return separatorIndex > 0 && separatorIndex < header.Length - 1;
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
}

/// <summary>
/// Represents a failure produced by <see cref="DecorationDiagnostics.Validate(System.Reflection.Assembly?, DecorationScanOptions?)"/>.
/// </summary>
public sealed class DecorationValidationException : InvalidOperationException
{
    /// <summary>
    /// Gets the aggregated diagnostics that caused the failure.
    /// </summary>
    public ImmutableList<DecorationDiagnostic> Diagnostics { get; }

    public DecorationValidationException(ImmutableList<DecorationDiagnostic> diagnostics)
        : base(BuildMessage(diagnostics))
    {
        Diagnostics = diagnostics;
    }

    private static string BuildMessage(ImmutableList<DecorationDiagnostic> diagnostics)
    {
        var lines = diagnostics.Select(d => $"[{d.Severity}] {d.Kind} {d.TypeName}: {d.Message}");
        return "Decoration validation failed:\n" + string.Join(Environment.NewLine, lines);
    }
}


