using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DiDecoration.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class DecorationRegistrationGenerator : IIncrementalGenerator
{
    private const string SingletonServiceAttributeName = "SingletonServiceAttribute";
    private const string ScopedServiceAttributeName = "ScopedServiceAttribute";
    private const string TransientServiceAttributeName = "TransientServiceAttribute";
    private const string BackgroundServiceAttributeName = "BackgroundServiceAttribute";
    private const string HttpClientServiceAttributeName = "HttpClientServiceAttribute";
    private const string OptionAttributeName = "OptionAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDecorations = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                transform: static (syntaxContext, cancellationToken) => TryCreateClassDecorations(syntaxContext, cancellationToken))
            .Where(static decorations => decorations is not null)
            .Select(static (decorations, _) => decorations!);

        context.RegisterSourceOutput(classDecorations.Collect(), static (sourceContext, decorations) =>
        {
            sourceContext.AddSource(
                "DiDecoration.Generators.DecorationRegistrationGenerator.g.cs",
                SourceText.From(GenerateSource(decorations), Encoding.UTF8));
        });
    }

    private static ClassDecorations? TryCreateClassDecorations(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.Node is not ClassDeclarationSyntax classSyntax)
        {
            return null;
        }

        if (context.SemanticModel.GetDeclaredSymbol(classSyntax, cancellationToken) is not INamedTypeSymbol typeSymbol)
        {
            return null;
        }

        var services = ImmutableArray.CreateBuilder<ServiceRegistration>();
        var hostedServices = ImmutableArray.CreateBuilder<HostedServiceRegistration>();
        var httpClients = ImmutableArray.CreateBuilder<HttpClientRegistration>();
        var options = ImmutableArray.CreateBuilder<OptionRegistration>();

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.Name;
            switch (attributeName)
            {
                case SingletonServiceAttributeName:
                    AddServiceRegistrations(typeSymbol, attribute, "Singleton", services);
                    break;
                case ScopedServiceAttributeName:
                    AddServiceRegistrations(typeSymbol, attribute, "Scoped", services);
                    break;
                case TransientServiceAttributeName:
                    AddServiceRegistrations(typeSymbol, attribute, "Transient", services);
                    break;
                case BackgroundServiceAttributeName:
                    AddHostedServiceRegistration(typeSymbol, attribute, hostedServices);
                    break;
                case HttpClientServiceAttributeName:
                    AddHttpClientRegistration(typeSymbol, attribute, httpClients);
                    break;
                case OptionAttributeName:
                    AddOptionRegistration(typeSymbol, attribute, options);
                    break;
            }
        }

        if (services.Count == 0 && hostedServices.Count == 0 && httpClients.Count == 0 && options.Count == 0)
        {
            return null;
        }

        return new ClassDecorations(
            GetTypeName(typeSymbol),
            services.ToImmutable(),
            hostedServices.ToImmutable(),
            httpClients.ToImmutable(),
            options.ToImmutable());
    }

    private static void AddServiceRegistrations(
        INamedTypeSymbol implementationType,
        AttributeData attribute,
        string lifetime,
        ImmutableArray<ServiceRegistration>.Builder registrations)
    {
        var serviceTypes = GetTypeArray(attribute, "ServiceTypes");
        if (serviceTypes.Length == 0)
        {
            serviceTypes = [implementationType];
        }

        var multiple = GetNamedBool(attribute, "Multiple") ?? false;

        foreach (var serviceType in serviceTypes)
        {
            if (serviceType is null)
            {
                continue;
            }

            registrations.Add(new ServiceRegistration(
                ServiceType: GetTypeName(serviceType),
                ImplementationType: GetTypeName(implementationType),
                Lifetime: lifetime,
                Multiple: multiple));
        }
    }

    private static void AddHostedServiceRegistration(
        INamedTypeSymbol hostedType,
        AttributeData attribute,
        ImmutableArray<HostedServiceRegistration>.Builder registrations)
    {
        var serviceType = GetSingleType(attribute, "ServiceType");
        registrations.Add(new HostedServiceRegistration(
            HostedType: GetTypeName(hostedType),
            ServiceType: serviceType is null ? null : GetTypeName(serviceType)));
    }

    private static void AddHttpClientRegistration(
        INamedTypeSymbol clientType,
        AttributeData attribute,
        ImmutableArray<HttpClientRegistration>.Builder registrations)
    {
        var baseUrl = GetNamedString(attribute, "BaseUrl") ?? GetConstructorString(attribute, 0);
        var timeoutSeconds = GetNamedInt(attribute, "TimeoutSeconds") ?? GetConstructorInt(attribute, 1) ?? 30;
        var clientName = GetNamedString(attribute, "ClientName");
        var defaultHeaders = GetStringArray(attribute, "DefaultHeaders");
        var handlerTypes = GetTypeArray(attribute, "Handlers");
        if (handlerTypes.Length == 0)
        {
            handlerTypes = GetTypeArray(attribute, "Interceptors");
        }

        registrations.Add(new HttpClientRegistration(
            ClientType: GetTypeName(clientType),
            ClientName: clientName,
            BaseUrl: baseUrl,
            TimeoutSeconds: timeoutSeconds,
            DefaultHeaders: defaultHeaders,
            HandlerTypes: handlerTypes.Select(type => GetTypeName(type!)).ToImmutableArray()));
    }

    private static void AddOptionRegistration(
        INamedTypeSymbol optionType,
        AttributeData attribute,
        ImmutableArray<OptionRegistration>.Builder registrations)
    {
        var key = GetConstructorString(attribute, 0) ?? GetNamedString(attribute, "Key");
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var optionKey = key!;
        registrations.Add(new OptionRegistration(
            OptionType: GetTypeName(optionType),
            Key: optionKey));
    }

    private static ImmutableArray<ITypeSymbol?> GetTypeArray(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value is { Kind: TypedConstantKind.Array } namedArray)
        {
            return namedArray.Values.Select(value => value.Value as ITypeSymbol).ToImmutableArray();
        }

        foreach (var constructorArgument in attribute.ConstructorArguments)
        {
            if (constructorArgument.Kind == TypedConstantKind.Array)
            {
                return constructorArgument.Values.Select(value => value.Value as ITypeSymbol).ToImmutableArray();
            }
        }

        return [];
    }

    private static ITypeSymbol? GetSingleType(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value.Value is ITypeSymbol namedType)
        {
            return namedType;
        }

        return attribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol;
    }

    private static string? GetNamedString(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value.Value is string value)
        {
            return value;
        }

        return null;
    }

    private static bool? GetNamedBool(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value.Value is bool value)
        {
            return value;
        }

        return null;
    }

    private static int? GetNamedInt(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value.Value is int value)
        {
            return value;
        }

        return null;
    }

    private static string? GetConstructorString(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index ? attribute.ConstructorArguments[index].Value as string : null;

    private static int? GetConstructorInt(AttributeData attribute, int index)
        => attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is int value ? value : null;

    private static ImmutableArray<string> GetStringArray(AttributeData attribute, string namedArgumentName)
    {
        if (attribute.NamedArguments.FirstOrDefault(argument => argument.Key == namedArgumentName).Value is { Kind: TypedConstantKind.Array } namedArray)
        {
            return namedArray.Values.Select(value => value.Value as string).Where(value => value is not null).Select(value => value!).ToImmutableArray();
        }

        foreach (var constructorArgument in attribute.ConstructorArguments)
        {
            if (constructorArgument.Kind == TypedConstantKind.Array)
            {
                return constructorArgument.Values.Select(value => value.Value as string).Where(value => value is not null).Select(value => value!).ToImmutableArray();
            }
        }

        return [];
    }

    private static string GetTypeName(ITypeSymbol typeSymbol)
    {
        return typeSymbol switch
        {
            IArrayTypeSymbol arrayType => $"{GetTypeName(arrayType.ElementType)}[]",
            IPointerTypeSymbol pointerType => $"{GetTypeName(pointerType.PointedAtType)}*",
            INamedTypeSymbol namedType => GetNamedTypeName(namedType),
            ITypeParameterSymbol typeParameter => typeParameter.Name,
            _ => typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
        };
    }

    private static string GetNamedTypeName(INamedTypeSymbol namedType)
    {
        var builder = new StringBuilder();

        if (namedType.ContainingType is not null)
        {
            builder.Append(GetTypeName(namedType.ContainingType));
            builder.Append('.');
            builder.Append(namedType.Name);
        }
        else
        {
            builder.Append("global::");
            var containingNamespace = namedType.ContainingNamespace;
            if (containingNamespace is not null && !containingNamespace.IsGlobalNamespace)
            {
                builder.Append(containingNamespace.ToDisplayString());
                builder.Append('.');
            }

            builder.Append(namedType.Name);
        }

        if (namedType.IsGenericType)
        {
            var genericArguments = namedType.TypeArguments;
            var isDefinition = namedType.IsDefinition || namedType.IsUnboundGenericType;

            if (isDefinition)
            {
                builder.Append('<');
                builder.Append(string.Join(",", Enumerable.Repeat(string.Empty, namedType.Arity)));
                builder.Append('>');
            }
            else
            {
                builder.Append('<');
                builder.Append(string.Join(", ", genericArguments.Select(argument => argument is null ? string.Empty : GetTypeName(argument))));
                builder.Append('>');
            }
        }

        return builder.ToString();
    }

    private static string GenerateSource(ImmutableArray<ClassDecorations> classDecorations)
    {
        var services = classDecorations.SelectMany(item => item.Services)
            .OrderBy(item => item.ImplementationType, StringComparer.Ordinal)
            .ThenBy(item => item.ServiceType, StringComparer.Ordinal)
            .ToImmutableArray();

        var hostedServices = classDecorations.SelectMany(item => item.HostedServices)
            .OrderBy(item => item.HostedType, StringComparer.Ordinal)
            .ToImmutableArray();

        var httpClients = classDecorations.SelectMany(item => item.HttpClients)
            .OrderBy(item => item.ClientType, StringComparer.Ordinal)
            .ToImmutableArray();

        var options = classDecorations.SelectMany(item => item.Options)
            .OrderBy(item => item.OptionType, StringComparer.Ordinal)
            .ToImmutableArray();

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated />");
        source.AppendLine("#nullable enable");
        source.AppendLine();
        source.AppendLine("using global::Microsoft.Extensions.Configuration;");
        source.AppendLine("using global::Microsoft.Extensions.DependencyInjection;");
        source.AppendLine("using global::Microsoft.Extensions.DependencyInjection.Extensions;");
        source.AppendLine("using global::System;");
        source.AppendLine();
        source.AppendLine("namespace DiDecoration.Generated;");
        source.AppendLine();
        source.AppendLine("public static class DecorationRegistrationExtensions");
        source.AppendLine("{");
        AppendRegisterServices(source, services);
        AppendRegisterHostedServices(source, hostedServices);
        AppendRegisterHttpClients(source, httpClients);
        AppendRegisterOptions(source, options);
        AppendRegisterDecorators(source);
        source.AppendLine("}");

        return source.ToString();
    }

    private static void AppendRegisterServices(StringBuilder source, ImmutableArray<ServiceRegistration> services)
    {
        source.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterServicesGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.AppendLine("    {");
        if (services.IsDefaultOrEmpty)
        {
            source.AppendLine("        return services;");
            source.AppendLine("    }");
            source.AppendLine();
            return;
        }

        foreach (var service in services)
        {
            var descriptor = $"new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({service.ServiceType}), typeof({service.ImplementationType}), global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.{service.Lifetime})";
            source.AppendLine(service.Multiple
                ? $"        services.Add({descriptor});"
                : $"        services.TryAdd({descriptor});");
        }

        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void AppendRegisterHostedServices(StringBuilder source, ImmutableArray<HostedServiceRegistration> hostedServices)
    {
        source.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterHostedServicesGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.AppendLine("    {");
        foreach (var hostedService in hostedServices)
        {
            if (hostedService.ServiceType is null)
            {
                source.AppendLine($"        services.AddSingleton<global::Microsoft.Extensions.Hosting.IHostedService>(sp => (global::Microsoft.Extensions.Hosting.IHostedService)(sp.GetService(typeof({hostedService.HostedType})) ?? global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(sp, typeof({hostedService.HostedType}))));");
            }
            else
            {
                source.AppendLine($"        services.AddSingleton<global::Microsoft.Extensions.Hosting.IHostedService>(sp => (global::Microsoft.Extensions.Hosting.IHostedService)sp.GetRequiredService(typeof({hostedService.ServiceType}))); ");
            }
        }

        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void AppendRegisterHttpClients(StringBuilder source, ImmutableArray<HttpClientRegistration> httpClients)
    {
        source.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterHttpClientsGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
        source.AppendLine("    {");
        foreach (var httpClient in httpClients)
        {
            if (!string.IsNullOrWhiteSpace(httpClient.ClientName))
            {
                source.AppendLine($"        global::Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient<{httpClient.ClientType}>(services, {FormatStringLiteral(httpClient.ClientName!)}, client =>");
            }
            else
            {
                source.AppendLine($"        global::Microsoft.Extensions.DependencyInjection.HttpClientFactoryServiceCollectionExtensions.AddHttpClient<{httpClient.ClientType}>(services, client =>");
            }

            source.AppendLine("        {");
            if (!string.IsNullOrWhiteSpace(httpClient.BaseUrl))
            {
                source.AppendLine($"            client.BaseAddress = new global::System.Uri({FormatStringLiteral(httpClient.BaseUrl!)});");
            }

            source.AppendLine($"            client.Timeout = global::System.TimeSpan.FromSeconds({httpClient.TimeoutSeconds.ToString(CultureInfo.InvariantCulture)});");
            foreach (var header in httpClient.DefaultHeaders)
            {
                var separatorIndex = header.IndexOf('=');
                if (separatorIndex < 0)
                {
                    separatorIndex = header.IndexOf(':');
                }

                if (separatorIndex <= 0 || separatorIndex == header.Length - 1)
                {
                    continue;
                }

                var headerName = header.Substring(0, separatorIndex).Trim();
                var headerValue = header.Substring(separatorIndex + 1).Trim();
                source.AppendLine($"            client.DefaultRequestHeaders.TryAddWithoutValidation({FormatStringLiteral(headerName)}, {FormatStringLiteral(headerValue)});");
            }
            source.AppendLine("        })");

            foreach (var handlerType in httpClient.HandlerTypes)
            {
                source.AppendLine($"            .AddHttpMessageHandler<{handlerType}>()");
            }

            source.AppendLine("            ;");
        }

        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void AppendRegisterOptions(StringBuilder source, ImmutableArray<OptionRegistration> options)
    {
        source.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterOptionsGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.Configuration.IConfiguration configuration)");
        source.AppendLine("    {");
        foreach (var option in options)
        {
            source.AppendLine($"        services.Configure<{option.OptionType}>(configuration.GetSection({FormatStringLiteral(option.Key)}));");
        }

        source.AppendLine("        return services;");
        source.AppendLine("    }");
        source.AppendLine();
    }

    private static void AppendRegisterDecorators(StringBuilder source)
    {
        source.AppendLine("    public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection RegisterDecoratorsGenerated(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services, global::Microsoft.Extensions.Configuration.IConfiguration? configuration = null)");
        source.AppendLine("    {");
        source.AppendLine("        services.RegisterServicesGenerated();");
        source.AppendLine("        services.RegisterHostedServicesGenerated();");
        source.AppendLine("        services.RegisterHttpClientsGenerated();");
        source.AppendLine("        if (configuration is not null)");
        source.AppendLine("        {");
        source.AppendLine("            services.RegisterOptionsGenerated(configuration);");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        return services;");
        source.AppendLine("    }");
    }

    private static string FormatStringLiteral(string value)
        => SymbolDisplay.FormatLiteral(value, true);

    private sealed record ClassDecorations(
        string TypeName,
        ImmutableArray<ServiceRegistration> Services,
        ImmutableArray<HostedServiceRegistration> HostedServices,
        ImmutableArray<HttpClientRegistration> HttpClients,
        ImmutableArray<OptionRegistration> Options);

    private sealed record ServiceRegistration(
        string ServiceType,
        string ImplementationType,
        string Lifetime,
        bool Multiple);

    private sealed record HostedServiceRegistration(
        string HostedType,
        string? ServiceType);

    private sealed record HttpClientRegistration(
        string ClientType,
        string? ClientName,
        string? BaseUrl,
        int TimeoutSeconds,
        ImmutableArray<string> DefaultHeaders,
        ImmutableArray<string> HandlerTypes);

    private sealed record OptionRegistration(
        string OptionType,
        string Key);
}





