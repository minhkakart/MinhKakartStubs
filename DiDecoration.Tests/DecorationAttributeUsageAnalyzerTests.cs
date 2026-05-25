using System.Collections.Immutable;
using DiDecoration.Analyzers;
using DiDecoration.Extensions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace DiDecoration.Tests;

public sealed class DecorationAttributeUsageAnalyzerTests
{
    [Fact]
    public async Task Analyzer_reports_invalid_attribute_usage()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(InvalidUsageSource);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DDI001");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DDI002");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DDI003");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DDI004");
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("does not implement the service type", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("must not use a non-singleton ServiceAttribute", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("absolute URI", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("constructor that accepts HttpClient", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("must use the format Name=Value", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("must not be empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Analyzer_does_not_report_diagnostics_for_valid_attribute_usage()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(ValidUsageSource);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "AnalyzerSample",
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new DecorationAttributeUsageAnalyzer();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(analyzer);
        var result = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return result;
    }

    private static ImmutableArray<MetadataReference> GetReferences()
    {
        var referencePaths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        referencePaths.Add(typeof(ServiceCollectionExtensions).Assembly.Location);

        return referencePaths
            .Select(MetadataReference (path) => MetadataReference.CreateFromFile(path))
            .ToImmutableArray();
    }

    private const string InvalidUsageSource = """
using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace AnalyzerSamples;

public interface IWrongService { }

[SingletonService(typeof(IWrongService))]
public sealed class InvalidService
{
}

public interface IWrongHostedService { }

[ScopedService]
[BackgroundService(typeof(IWrongHostedService))]
public sealed class InvalidHostedWorker : HostedServiceBase
{
}

public sealed class InvalidHttpClientUsage
{
}

[HttpClientService("not-a-url", 15, typeof(NotAHandler), DefaultHeaders = new[] { "BrokenHeader" })]
public sealed class InvalidHttpClient
{
    public InvalidHttpClient(string value)
    {
    }
}

public sealed class NotAHandler
{
}

[Option("")]
public sealed class InvalidOptions
{
}

public abstract class HostedServiceBase : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
""";

    private const string ValidUsageSource = """
using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

namespace AnalyzerSamples;

public interface IValidService { }

[SingletonService(typeof(IValidService))]
public sealed class ValidService : IValidService
{
}

public interface IValidHostedService { }

[SingletonService(typeof(IValidHostedService))]
[BackgroundService(typeof(IValidHostedService))]
public sealed class ValidHostedWorker : HostedServiceBase, IValidHostedService
{
}

[HttpClientService("https://api.example.com", 15, typeof(ValidHandler), ClientName = "catalog-client", DefaultHeaders = new[] { "X-App=Valid" })]
public sealed class ValidHttpClient
{
    public ValidHttpClient(HttpClient httpClient)
    {
    }
}

public sealed class ValidHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
}

[Option("ValidOptions")]
public sealed class ValidOptions
{
    public string? Name { get; set; }
}

public abstract class HostedServiceBase : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
""";
}



