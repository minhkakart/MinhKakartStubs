# DiDecoration

Attribute-driven helpers for registering services in `Microsoft.Extensions.DependencyInjection`.

## Quick start

```csharp
using DiDecoration.Extensions;

services
    .RegisterServices(typeof(MyService).Assembly)
    .RegisterHostedServices(typeof(MyWorker).Assembly)
    .RegisterHttpClients(typeof(CatalogClient).Assembly)
    .RegisterOptions(configuration, typeof(MyOptions).Assembly);

services.RegisterDecorators(configuration, typeof(MyService).Assembly);
```

## Core examples

### Register a focused assembly slice

```csharp
services.RegisterDecorators(
    configuration,
    typeof(MyFeatureMarker).Assembly,
    new DecorationScanOptions
    {
        NamespacePrefix = "MyApp.Features.Billing",
        IncludeInternalTypes = true,
        Predicate = type => type.Name.EndsWith("Service", StringComparison.Ordinal)
    });
```

### Register services in a predictable order

```csharp
services
    .RegisterServices(typeof(WorkerDependencies).Assembly)
    .RegisterHostedServices(typeof(WorkerDependencies).Assembly);
```

### Validate a scan before registering

```csharp
var diagnostics = DecorationDiagnostics.Analyze(typeof(MyFeatureMarker).Assembly);

if (diagnostics.Any(item => item.Severity == DecorationDiagnosticSeverity.Error))
{
    throw new InvalidOperationException("Fix attribute usage before continuing.");
}

DecorationDiagnostics.Validate(typeof(MyFeatureMarker).Assembly);
```

## Example attributes

```csharp
[SingletonService(typeof(IMyService))]
public sealed class MyService : IMyService
{
}

[BackgroundService]
public sealed class MyWorker : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
}

[HttpClientService("https://api.example.com", 30, ClientName = "catalog-client")]
public sealed class CatalogClient
{
    public CatalogClient(HttpClient httpClient)
    {
    }
}

[Option("MyOptions")]
public sealed class MyOptions
{
    public string? Name { get; set; }
}
```

## More docs

- [Documentation home](docs/README.md)
- [Guides index](docs/guides/README.md)
- [Release notes](docs/releases/README.md)
- [NuGet publishing guide](docs/guides/nuget-publishing/README.md)

