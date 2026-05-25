using DiDecoration.Attributes;
using DiDecoration.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DiDecoration.Tests;

public sealed class Stage4GeneratorTests
{
    [Fact]
    public void DecorationRegistrationGenerator_emits_registration_helpers_for_supported_attributes()
    {
        var compilation = CSharpCompilation.Create(
            assemblyName: "Stage4GeneratorSample",
            syntaxTrees: [CSharpSyntaxTree.ParseText(SampleSource, new CSharpParseOptions(LanguageVersion.Latest))],
            references: GetMetadataReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new DecorationRegistrationGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.Empty(diagnostics);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var runResult = driver.GetRunResult();
        var generatedSource = Assert.Single(runResult.Results).GeneratedSources.Single().SourceText.ToString();

        Assert.Contains("RegisterServicesGenerated", generatedSource);
        Assert.Contains("RegisterHostedServicesGenerated", generatedSource);
        Assert.Contains("RegisterHttpClientsGenerated", generatedSource);
        Assert.Contains("RegisterOptionsGenerated", generatedSource);
        Assert.Contains("RegisterDecoratorsGenerated", generatedSource);
        Assert.Contains("typeof(global::Sample.IWidget)", generatedSource);
        Assert.Contains("typeof(global::Sample.IOpenGenericRepo<>)", generatedSource);
        Assert.Contains("GetRequiredService(typeof(global::Sample.IWorkerContract))", generatedSource);
        Assert.Contains("AddHttpClient<global::Sample.CatalogClient>(services, \"sample-client\"", generatedSource);
        Assert.Contains("services.Configure<global::Sample.SampleOptions>(configuration.GetSection(\"Sample:Options\"));", generatedSource);
    }

    private static IEnumerable<MetadataReference> GetMetadataReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        paths.Add(typeof(ServiceAttribute).Assembly.Location);
        paths.Add(typeof(IServiceCollection).Assembly.Location);
        paths.Add(typeof(CSharpCompilation).Assembly.Location);
        paths.Add(typeof(Enumerable).Assembly.Location);
        paths.Add(typeof(Task).Assembly.Location);
        paths.Add(typeof(HttpClient).Assembly.Location);
        AddIfFound(paths, "Microsoft.Extensions.DependencyInjection.Abstractions.dll");
        AddIfFound(paths, "Microsoft.Extensions.Configuration.Abstractions.dll");
        AddIfFound(paths, "Microsoft.Extensions.Hosting.Abstractions.dll");
        AddIfFound(paths, "Microsoft.Extensions.Http.dll");
        AddIfFound(paths, "Microsoft.Extensions.Options.ConfigurationExtensions.dll");

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static void AddIfFound(List<string> paths, string fileName)
    {
        var match = Directory.EnumerateFiles(AppContext.BaseDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(match))
        {
            paths.Add(match);
        }
    }

    private const string SampleSource = """
using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sample;

public interface IWidget { }

[SingletonService(typeof(IWidget))]
public sealed class Widget : IWidget
{
}

public interface IOpenGenericRepo<T>
{
}

[ScopedService(typeof(IOpenGenericRepo<>))]
public sealed class OpenGenericRepo<T> : IOpenGenericRepo<T>
{
}

public interface IWorkerContract
{
}

[SingletonService(typeof(IWorkerContract))]
[BackgroundService(typeof(IWorkerContract))]
public sealed class Worker : IWorkerContract, IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[HttpClientService("https://api.example.com", 12, typeof(LoggingHandler), ClientName = "sample-client", DefaultHeaders = new[] { "X-Test=1" })]
public sealed class CatalogClient
{
    public CatalogClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

public sealed class LoggingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
}

[Option("Sample:Options")]
public sealed class SampleOptions
{
    public string? Label { get; set; }
}
""";
}





