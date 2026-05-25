using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DiDecoration.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DecorationAttributeUsageAnalyzer : DiagnosticAnalyzer
{
    private const string ServiceAttributeName = "DiDecoration.Attributes.ServiceAttribute";
    private const string BackgroundServiceAttributeName = "DiDecoration.Attributes.BackgroundServiceAttribute";
    private const string HttpClientServiceAttributeName = "DiDecoration.Attributes.HttpClientServiceAttribute";
    private const string OptionAttributeName = "DiDecoration.Attributes.OptionAttribute";
    private const string DelegatingHandlerName = "System.Net.Http.DelegatingHandler";
    private const string HttpClientName = "System.Net.Http.HttpClient";
    private const string IHostedServiceName = "Microsoft.Extensions.Hosting.IHostedService";

    private static readonly DiagnosticDescriptor InvalidServiceMappingRule = new(
        "DDI001",
        "Invalid service mapping",
        "{0}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHostedServiceRule = new(
        "DDI002",
        "Invalid hosted service attribute usage",
        "{0}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidHttpClientRule = new(
        "DDI003",
        "Invalid typed HTTP client attribute usage",
        "{0}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidOptionRule = new(
        "DDI004",
        "Invalid option attribute usage",
        "{0}",
        "Usage",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
        InvalidServiceMappingRule,
        InvalidHostedServiceRule,
        InvalidHttpClientRule,
        InvalidOptionRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class)
        {
            return;
        }

        AnalyzeServiceAttributes(type, context);
        AnalyzeHostedServiceAttribute(type, context);
        AnalyzeHttpClientAttribute(type, context);
        AnalyzeOptionAttribute(type, context);
    }

    private static void AnalyzeServiceAttributes(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        foreach (var attribute in type.GetAttributes().Where(attribute => InheritsFrom(attribute.AttributeClass, ServiceAttributeName)))
        {
            foreach (var serviceType in GetServiceTypes(attribute))
            {
                if (serviceType is null)
                {
                    Report(context, InvalidServiceMappingRule, attribute, "ServiceAttribute contains a null service type.");
                    continue;
                }

                if (!IsValidServiceMapping(type, serviceType))
                {
                    Report(context, InvalidServiceMappingRule, attribute,
                        $"Cannot register {type.ToDisplayString()} as {serviceType.ToDisplayString()} because it does not implement the service type.");
                }
            }
        }
    }

    private static void AnalyzeHostedServiceAttribute(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        var attribute = type.GetAttributes().FirstOrDefault(attribute => InheritsFrom(attribute.AttributeClass, BackgroundServiceAttributeName));
        if (attribute is null)
        {
            return;
        }

        if (!Implements(type, IHostedServiceName))
        {
            Report(context, InvalidHostedServiceRule, attribute,
                $"{type.ToDisplayString()} is marked with BackgroundServiceAttribute but does not implement IHostedService.");
        }

        var serviceType = GetFirstTypeArgument(attribute);
        if (serviceType is not null)
        {
            if (!serviceType.Equals(type, SymbolEqualityComparer.Default) && (serviceType.TypeKind != TypeKind.Interface || !Implements(type, serviceType)))
            {
                Report(context, InvalidHostedServiceRule, attribute,
                    "BackgroundServiceAttribute.ServiceType must be an interface implemented by the hosted service or the hosted service type itself.");
            }
        }

        if (type.GetAttributes().Any(attributeData =>
                InheritsFrom(attributeData.AttributeClass, ServiceAttributeName) &&
                attributeData.AttributeClass?.Name is not "SingletonServiceAttribute"))
        {
            Report(context, InvalidHostedServiceRule, attribute,
                $"Hosted service {type.ToDisplayString()} must not use a non-singleton ServiceAttribute.");
        }
    }

    private static void AnalyzeHttpClientAttribute(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        var attribute = type.GetAttributes().FirstOrDefault(attribute => InheritsFrom(attribute.AttributeClass, HttpClientServiceAttributeName));
        if (attribute is null)
        {
            return;
        }

        var baseUrl = GetConstructorStringArgument(attribute, 0);
        if (!string.IsNullOrWhiteSpace(baseUrl) && !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            Report(context, InvalidHttpClientRule, attribute, "BaseUrl must be an absolute URI.");
        }

        if (!HasPublicHttpClientConstructor(type))
        {
            Report(context, InvalidHttpClientRule, attribute, "The typed client must expose a public constructor that accepts HttpClient.");
        }

        foreach (var handlerType in GetHandlerTypes(attribute))
        {
            if (handlerType is null)
            {
                Report(context, InvalidHttpClientRule, attribute, "A handler type is null.");
                continue;
            }

            if (!ImplementsOrDerivesFrom(handlerType, DelegatingHandlerName))
            {
                Report(context, InvalidHttpClientRule, attribute,
                    $"Handler type {handlerType.ToDisplayString()} does not inherit from DelegatingHandler.");
            }
        }

        foreach (var header in GetDefaultHeaders(attribute))
        {
            if (!IsValidHeaderDefinition(header))
            {
                Report(context, InvalidHttpClientRule, attribute,
                    $"Default header '{header}' must use the format Name=Value.");
            }
        }
    }

    private static void AnalyzeOptionAttribute(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        foreach (var attribute in type.GetAttributes().Where(attribute => InheritsFrom(attribute.AttributeClass, OptionAttributeName)))
        {
            var key = GetConstructorStringArgument(attribute, 0);
            if (string.IsNullOrWhiteSpace(key))
            {
                Report(context, InvalidOptionRule, attribute, "OptionAttribute key must not be empty.");
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol?> GetServiceTypes(AttributeData attribute)
    {
        if (TryGetNamedTypeArray(attribute, "ServiceTypes", out var namedServiceTypes))
        {
            return namedServiceTypes;
        }

        if (attribute.ConstructorArguments.Length == 0)
        {
            return [];
        }

        if (attribute.ConstructorArguments.Length == 1)
        {
            return attribute.ConstructorArguments[0].Kind == TypedConstantKind.Array
                ? attribute.ConstructorArguments[0].Values.Select(value => value.Value as INamedTypeSymbol)
                : [];
        }

        if (attribute.ConstructorArguments.Length >= 2 && attribute.ConstructorArguments[1].Value is INamedTypeSymbol singleServiceType)
        {
            return [singleServiceType];
        }

        return [];
    }

    private static INamedTypeSymbol? GetFirstTypeArgument(AttributeData attribute)
        => attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as INamedTypeSymbol : null;

    private static string? GetConstructorStringArgument(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as string : null;

    private static IEnumerable<INamedTypeSymbol?> GetHandlerTypes(AttributeData attribute)
    {
        if (TryGetNamedTypeArray(attribute, "Handlers", out var namedHandlers))
        {
            return namedHandlers;
        }

        if (attribute.ConstructorArguments.Length >= 3 && attribute.ConstructorArguments[2].Kind == TypedConstantKind.Array)
        {
            return attribute.ConstructorArguments[2].Values.Select(value => value.Value as INamedTypeSymbol);
        }

        return [];
    }

    private static IEnumerable<string> GetDefaultHeaders(AttributeData attribute)
    {
        if (TryGetNamedStringArray(attribute, "DefaultHeaders", out var namedHeaders))
        {
            return namedHeaders;
        }

        return [];
    }

    private static bool HasPublicHttpClientConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Any(parameter => parameter.Type.ToDisplayString() == HttpClientName));

    private static bool IsValidHeaderDefinition(string header)
    {
        var separatorIndex = header.IndexOf('=');
        if (separatorIndex < 0)
        {
            separatorIndex = header.IndexOf(':');
        }

        return separatorIndex > 0 && separatorIndex < header.Length - 1;
    }

    private static bool IsValidServiceMapping(INamedTypeSymbol serviceClass, INamedTypeSymbol serviceType)
    {
        if (SymbolEqualityComparer.Default.Equals(serviceClass, serviceType))
        {
            return true;
        }

        if (serviceType.TypeKind == TypeKind.Interface)
        {
            return Implements(serviceClass, serviceType);
        }

        var current = serviceClass.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, serviceType))
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool ImplementsOrDerivesFrom(INamedTypeSymbol type, string metadataName)
        => InheritsFrom(type, metadataName) || Implements(type, metadataName);

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol interfaceType)
        => type.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, interfaceType) || SymbolEqualityComparer.Default.Equals(@interface.OriginalDefinition, interfaceType.OriginalDefinition));

    private static bool Implements(INamedTypeSymbol type, string metadataName)
        => type.AllInterfaces.Any(@interface => @interface.ToDisplayString() == metadataName || @interface.OriginalDefinition.ToDisplayString() == metadataName);

    private static bool TryGetNamedTypeArray(AttributeData attribute, string name, out IEnumerable<INamedTypeSymbol?> values)
    {
        if (attribute.NamedArguments.FirstOrDefault(named => named.Key == name) is { Value.Kind: TypedConstantKind.Array } namedArgument)
        {
            values = namedArgument.Value.Values.Select(value => value.Value as INamedTypeSymbol);
            return true;
        }

        values = [];
        return false;
    }

    private static bool TryGetNamedStringArray(AttributeData attribute, string name, out IEnumerable<string> values)
    {
        if (attribute.NamedArguments.FirstOrDefault(named => named.Key == name) is { Value.Kind: TypedConstantKind.Array } namedArgument)
        {
            values = namedArgument.Value.Values.Select(value => value.Value as string).Where(value => value is not null).Select(value => value!);
            return true;
        }

        values = [];
        return false;
    }

    private static bool InheritsFrom(INamedTypeSymbol? symbol, string metadataName)
    {
        while (symbol is not null)
        {
            if (symbol.ToDisplayString() == metadataName)
            {
                return true;
            }

            symbol = symbol.BaseType;
        }

        return false;
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, AttributeData attribute, string message)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
    }
}


