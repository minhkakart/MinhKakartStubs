using System.Reflection;
using System.Reflection.Emit;
using DiDecoration.Attributes;
using DiDecoration.Utils;
using Xunit;

namespace DiDecoration.Tests;

public sealed class Stage3DiagnosticsTests
{
    [Fact]
    public void Analyze_collects_multiple_issues_across_registration_categories()
    {
        var diagnostics = DecorationDiagnostics.Analyze(CreateDiagnosticsAssembly());

        Assert.Equal(6, diagnostics.Count);
        Assert.Contains(diagnostics, item => item.Kind == DecorationDiagnosticKind.Service && item.TypeName.Contains("DynamicInvalidStage3Service", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Kind == DecorationDiagnosticKind.HostedService && item.TypeName.Contains("DynamicInvalidStage3HostedWorker", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Kind == DecorationDiagnosticKind.HttpClient && item.TypeName.Contains("DynamicInvalidStage3HttpClient", StringComparison.Ordinal));
        Assert.Contains(diagnostics, item => item.Message.Contains("absolute URI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, item => item.Message.Contains("constructor that accepts HttpClient", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, item => item.Message.Contains("does not inherit from DelegatingHandler", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, item => item.Message.Contains("must use the format Name=Value", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_throws_aggregate_exception_with_all_errors()
    {
        var exception = Assert.Throws<DecorationValidationException>(() => DecorationDiagnostics.Validate(CreateDiagnosticsAssembly()));

        Assert.Equal(6, exception.Diagnostics.Count);
        Assert.Contains("Decoration validation failed", exception.Message);
        Assert.Contains(exception.Diagnostics, item => item.Kind == DecorationDiagnosticKind.HttpClient && item.Severity == DecorationDiagnosticSeverity.Error);
    }

    private static Assembly CreateDiagnosticsAssembly()
    {
        var assemblyName = new AssemblyName($"Stage3Diagnostics_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

        var serviceType = moduleBuilder.DefineType("DynamicInvalidStage3Service", TypeAttributes.Public | TypeAttributes.Class);
        serviceType.SetCustomAttribute(CreateSingletonServiceAttribute(typeof(IInvalidStage3Service)));
        serviceType.DefineDefaultConstructor(MethodAttributes.Public);
        _ = serviceType.CreateTypeInfo();

        var hostedType = moduleBuilder.DefineType("DynamicInvalidStage3HostedWorker", TypeAttributes.Public | TypeAttributes.Class, typeof(Stage3HostedServiceBase));
        hostedType.SetCustomAttribute(CreateBackgroundServiceAttribute(typeof(IInvalidStage3HostedService)));
        hostedType.DefineDefaultConstructor(MethodAttributes.Public);
        _ = hostedType.CreateTypeInfo();

        var httpClientType = moduleBuilder.DefineType("DynamicInvalidStage3HttpClient", TypeAttributes.Public | TypeAttributes.Class);
        httpClientType.SetCustomAttribute(CreateHttpClientAttribute("not-a-url", [typeof(Stage3NotAHandler)], ["BrokenHeader"]));
        httpClientType.DefineDefaultConstructor(MethodAttributes.Public);
        _ = httpClientType.CreateTypeInfo();

        return assemblyBuilder;
    }

    private static CustomAttributeBuilder CreateSingletonServiceAttribute(params Type[] serviceTypes)
    {
        var ctor = typeof(SingletonServiceAttribute).GetConstructor([typeof(Type[])])!;
        return new CustomAttributeBuilder(ctor, [serviceTypes]);
    }

    private static CustomAttributeBuilder CreateBackgroundServiceAttribute(Type? serviceType)
    {
        var ctor = typeof(BackgroundServiceAttribute).GetConstructor([typeof(Type)])!;
        return new CustomAttributeBuilder(ctor, [serviceType]);
    }

    private static CustomAttributeBuilder CreateHttpClientAttribute(string? baseUrl, Type[] interceptors, string[] defaultHeaders)
    {
        var ctor = typeof(HttpClientServiceAttribute).GetConstructor([typeof(string), typeof(int), typeof(Type[])])!;
        var property = typeof(HttpClientServiceAttribute).GetProperty(nameof(HttpClientServiceAttribute.DefaultHeaders))!;
        return new CustomAttributeBuilder(ctor, [baseUrl, 10, interceptors], [property], [defaultHeaders]);
    }
}



