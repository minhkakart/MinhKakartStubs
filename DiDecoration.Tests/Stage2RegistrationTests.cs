using DiDecoration.Extensions;
using DiDecoration.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiDecoration.Tests.Stage2;

public sealed class Stage2RegistrationTests
{
    [Fact]
    public void RegisterServices_applies_namespace_predicate_and_internal_filters()
    {
        var services = new ServiceCollection();
        var scanOptions = new DecorationScanOptions
        {
            NamespacePrefix = typeof(Filtering.ScanNamespaceVisibleService).Namespace,
            IncludeInternalTypes = false,
            Predicate = type => type.Name.Contains("Visible", StringComparison.Ordinal)
        };

        services.RegisterServices(typeof(Filtering.ScanNamespaceVisibleService).Assembly, scanOptions);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<Filtering.ScanNamespaceVisibleService>());
        Assert.Null(provider.GetService<Filtering.ScanNamespaceHiddenService>());
        Assert.Null(provider.GetService<Filtering.ScanNamespaceInternalService>());
    }

    [Fact]
    public void RegisterServices_supports_open_generic_service_mappings()
    {
        var services = new ServiceCollection();

        services.RegisterServices(typeof(Generic.OpenGenericRepository<>).Assembly);

        using var provider = services.BuildServiceProvider();
        var repository = provider.GetRequiredService<Generic.IOpenGenericRepository<int>>();

        Assert.IsType<Generic.OpenGenericRepository<int>>(repository);
    }

    [Fact]
    public void RegisterHttpClients_supports_client_name_override_default_headers_and_handler_override()
    {
        var services = new ServiceCollection();
        services.AddHttpClient("custom-client", client => client.DefaultRequestHeaders.Add("X-Configured", "true"));

        services.RegisterHttpClients(typeof(Http.EnhancedCatalogClient).Assembly);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<Http.EnhancedCatalogClient>();

        Assert.Equal(new Uri("https://api.example.com/"), client.HttpClient.BaseAddress);
        Assert.True(client.HttpClient.DefaultRequestHeaders.TryGetValues("X-App", out var appValues));
        Assert.Contains("Stage2", appValues);
        Assert.True(client.HttpClient.DefaultRequestHeaders.TryGetValues("X-Configured", out var configuredValues));
        Assert.Contains("true", configuredValues);
    }

    [Fact]
    public void RegisterDecorators_registers_all_decorator_types_in_one_call()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AggregateOptions:Label"] = "Ready"
            })
            .Build();

        var scanOptions = new DecorationScanOptions
        {
            NamespacePrefix = typeof(Aggregate.AggregateWorker).Namespace
        };

        services.RegisterDecorators(configuration, typeof(Aggregate.AggregateWorker).Assembly, scanOptions);

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<Aggregate.IAggregateWorker>();
        var hostedService = provider.GetServices<IHostedService>().OfType<Aggregate.AggregateWorker>().Single();
        var client = provider.GetRequiredService<Aggregate.AggregateClient>();
        var options = provider.GetRequiredService<IOptions<Aggregate.AggregateOptions>>().Value;

        Assert.IsType<Aggregate.AggregateWorker>(service);
        Assert.Same(service, hostedService);
        Assert.Equal(new Uri("https://api.example.com/"), client.HttpClient.BaseAddress);
        Assert.Equal("Ready", options.Label);
    }
}



