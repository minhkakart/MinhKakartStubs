using System.Reflection;
using System.Reflection.Emit;
using DiDecoration.Attributes;
using DiDecoration.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace DiDecoration.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RegisterServices_registers_service_as_interface_and_self_when_declared()
    {
        var services = new ServiceCollection();

        services.RegisterServices(typeof(FooService).Assembly);

        using var provider = services.BuildServiceProvider();
        var foo = provider.GetRequiredService<IFooService>();
        var self = provider.GetRequiredService<SelfService>();

        Assert.IsType<FooService>(foo);
        Assert.IsType<SelfService>(self);
    }

    [Fact]
    public void RegisterServices_throws_for_invalid_service_mapping()
    {
        var assembly = CreateDynamicAssembly(
            "InvalidServiceMapping",
            typeBuilder => typeBuilder.SetCustomAttribute(CreateSingletonServiceAttribute(typeof(IInvalidServiceMapping))));

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.RegisterServices(assembly));
        Assert.Contains("does not implement the service type", exception.Message);
    }

    [Fact]
    public void RegisterServices_registers_keyed_service_mappings()
    {
        var services = new ServiceCollection();

        services.RegisterServices(typeof(KeyedService).Assembly);

        using var provider = services.BuildServiceProvider();
        var keyed = provider.GetRequiredKeyedService<IKeyedService>("primary");

        Assert.Null(provider.GetService<IKeyedService>());
        Assert.IsType<KeyedService>(keyed);
        Assert.Same(keyed, provider.GetRequiredKeyedService<IKeyedService>("primary"));
    }

    [Fact]
    public void RegisterHostedServices_registers_direct_hosted_service()
    {
        var services = new ServiceCollection();

        services.RegisterServices(typeof(DirectHostedService).Assembly);
        services.RegisterHostedServices(typeof(DirectHostedService).Assembly);

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().OfType<DirectHostedService>().Single();

        Assert.IsType<DirectHostedService>(hostedService);
    }

    [Fact]
    public void RegisterHostedServices_resolves_service_registered_after_the_hosted_service_registration()
    {
        var services = new ServiceCollection();

        services.RegisterHostedServices(typeof(OrderedWorker).Assembly);
        services.RegisterServices(typeof(OrderedWorker).Assembly);

        using var provider = services.BuildServiceProvider();
        var worker = provider.GetRequiredService<IOrderedWorker>();
        var hostedWorker = provider.GetServices<IHostedService>().OfType<OrderedWorker>().Single();

        Assert.Same(worker, hostedWorker);
    }

    [Fact]
    public void RegisterHostedServices_throws_for_invalid_service_type()
    {
        var assembly = CreateDynamicAssembly(
            "InvalidHostedService",
            typeBuilder =>
            {
                typeBuilder.SetParent(typeof(HostedServiceBase));
                typeBuilder.SetCustomAttribute(CreateBackgroundServiceAttribute(typeof(IInvalidHostedService)));
            });

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.RegisterHostedServices(assembly));
        Assert.Contains("must be an interface implemented by the hosted service", exception.Message);
    }

    [Fact]
    public void RegisterHostedServices_throws_for_non_singleton_lifetime_before_build()
    {
        var assembly = CreateDynamicAssembly(
            "InvalidHostedLifetime",
            typeBuilder =>
            {
                typeBuilder.SetParent(typeof(HostedServiceBase));
                typeBuilder.AddInterfaceImplementation(typeof(IInvalidLifetimeHostedService));
                typeBuilder.SetCustomAttribute(CreateTransientServiceAttribute(typeof(IInvalidLifetimeHostedService)));
                typeBuilder.SetCustomAttribute(CreateBackgroundServiceAttribute(typeof(IInvalidLifetimeHostedService)));
            });

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.RegisterHostedServices(assembly));
        Assert.Contains("hosted service", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterHostedServices_resolves_keyed_hosted_service()
    {
        var services = new ServiceCollection();

        services.RegisterServices(typeof(KeyedHostedWorker).Assembly);
        services.RegisterHostedServices(typeof(KeyedHostedWorker).Assembly);

        using var provider = services.BuildServiceProvider();
        var hostedWorker = provider.GetServices<IHostedService>().OfType<KeyedHostedWorker>().Single();
        var keyedWorker = provider.GetRequiredKeyedService<IKeyedHostedWorker>("worker-key");

        Assert.Same(keyedWorker, hostedWorker);
    }

    [Fact]
    public void RegisterHttpClients_registers_valid_typed_client()
    {
        var services = new ServiceCollection();

        services.RegisterHttpClients(typeof(CatalogClient).Assembly);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<CatalogClient>();

        Assert.Equal(new Uri("https://api.example.com/"), client.HttpClient.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(15), client.HttpClient.Timeout);
    }

    [Fact]
    public void RegisterHttpClients_throws_for_invalid_base_url()
    {
        var assembly = CreateDynamicAssembly(
            "InvalidHttpClientBaseUrl",
            typeBuilder => typeBuilder.SetCustomAttribute(CreateHttpClientAttribute("not-a-url", Array.Empty<Type>())));

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.RegisterHttpClients(assembly));
        Assert.Contains("BaseUrl must be an absolute URI", exception.Message);
    }

    [Fact]
    public void RegisterHttpClients_throws_for_invalid_interceptor_type()
    {
        var assembly = CreateDynamicAssembly(
            "InvalidHttpClientInterceptor",
            typeBuilder => typeBuilder.SetCustomAttribute(CreateHttpClientAttribute(null, [typeof(NotAHandler)])));

        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() => services.RegisterHttpClients(assembly));
        Assert.Contains("does not inherit from DelegatingHandler", exception.Message);
    }

    [Fact]
    public void RegisterOptions_binds_configured_options()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SampleOptions:Name"] = "DiDecoration",
                ["SampleOptions:Count"] = "42"
            })
            .Build();

        var services = new ServiceCollection();

        services.RegisterOptions(configuration, typeof(SampleOptions).Assembly);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SampleOptions>>().Value;

        Assert.Equal("DiDecoration", options.Name);
        Assert.Equal(42, options.Count);
    }

    private static Assembly CreateDynamicAssembly(string typeName, Action<TypeBuilder> configureType, Type? baseType = null)
    {
        var assemblyName = new AssemblyName($"Dynamic_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
        var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class, baseType ?? typeof(object));

        configureType(typeBuilder);

        typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);
        _ = typeBuilder.CreateTypeInfo();
        return assemblyBuilder;
    }

    private static CustomAttributeBuilder CreateSingletonServiceAttribute(params Type[] serviceTypes)
    {
        var ctor = typeof(SingletonServiceAttribute).GetConstructor([typeof(Type[])])!;
        return new CustomAttributeBuilder(ctor, [serviceTypes]);
    }

    private static CustomAttributeBuilder CreateTransientServiceAttribute(params Type[] serviceTypes)
    {
        var ctor = typeof(TransientServiceAttribute).GetConstructor([typeof(Type[])])!;
        return new CustomAttributeBuilder(ctor, [serviceTypes]);
    }

    private static CustomAttributeBuilder CreateBackgroundServiceAttribute(Type? serviceType)
    {
        var ctor = typeof(BackgroundServiceAttribute).GetConstructor([typeof(Type)])!;
        return new CustomAttributeBuilder(ctor, [serviceType]);
    }

    private static CustomAttributeBuilder CreateHttpClientAttribute(string? baseUrl, Type[] interceptors)
    {
        var ctor = typeof(HttpClientServiceAttribute).GetConstructor([typeof(string), typeof(int), typeof(Type[])])!;
        return new CustomAttributeBuilder(ctor, [baseUrl, 15, interceptors]);
    }
}

public interface IFooService
{
}

[SingletonService(typeof(IFooService))]
public sealed class FooService : IFooService
{
}

[TransientService]
public sealed class SelfService
{
}

public interface IOrderedWorker
{
}

[SingletonService(typeof(IOrderedWorker))]
[BackgroundService(typeof(IOrderedWorker))]
public sealed class OrderedWorker : HostedServiceBase, IOrderedWorker
{
}

[BackgroundService]
public sealed class DirectHostedService : HostedServiceBase
{
}

[HttpClientService("https://api.example.com", 15, typeof(RecordingHandler))]
public sealed class CatalogClient
{
    public CatalogClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

[Option("SampleOptions")]
public sealed class SampleOptions
{
    public string? Name { get; set; }
    public int Count { get; set; }
}

public abstract class HostedServiceBase : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class RecordingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

public sealed class NotAHandler
{
}

public interface IInvalidServiceMapping
{
}

public interface IInvalidHostedService
{
}

public interface IInvalidLifetimeHostedService
{
}

public interface IKeyedService
{
}

[SingletonService("primary", typeof(IKeyedService))]
public sealed class KeyedService : IKeyedService
{
}

public interface IKeyedHostedWorker
{
}

[SingletonService("worker-key", typeof(IKeyedHostedWorker))]
[BackgroundService(typeof(IKeyedHostedWorker), Key = "worker-key")]
public sealed class KeyedHostedWorker : HostedServiceBase, IKeyedHostedWorker
{
}




