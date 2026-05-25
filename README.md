# DiDecoration

Attribute-driven helpers for registering services in `Microsoft.Extensions.DependencyInjection`.

## Quick start

```csharp
using DiDecoration.Extensions;
using DiDecoration.Utils;

services
    .RegisterServices(typeof(MyService).Assembly)
    .RegisterHostedServices(typeof(MyWorker).Assembly)
    .RegisterHttpClients(typeof(CatalogClient).Assembly)
    .RegisterOptions(configuration, typeof(MyOptions).Assembly);

services.RegisterDecorators(configuration, typeof(MyService).Assembly, new DecorationScanOptions
{
    NamespacePrefix = "MyApp.Features",
    IncludeInternalTypes = false
});
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

[HttpClientService("https://api.example.com", 30, ClientName = "catalog-client", DefaultHeaders = new[] { "X-App=DiDecoration" })]
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

## Registration notes

- `RegisterServices` uses `TryAdd` for single registrations, so the first registration for a service type wins.
- Set `ServiceAttribute.Multiple = true` when you want additional implementations to remain in the collection.
- `RegisterHostedServices` can resolve a hosted service directly or through another registered service type.
- `RegisterHttpClients` validates base URLs and handler types before the client registration is added.
- `HttpClientServiceAttribute` supports client-name overrides, default request headers, and an explicit handler pipeline.
- `RegisterOptions` binds each `[Option]` class from the configuration section named by its key.
- `DecorationScanOptions` can filter by namespace prefix, predicate, and internal-type visibility.

## Diagnostics

Use `DecorationDiagnostics` when you want to inspect a scan before registering services:

```csharp
using DiDecoration.Utils;

var diagnostics = DecorationDiagnostics.Analyze(typeof(MyService).Assembly);

if (diagnostics.Any(item => item.Severity == DecorationDiagnosticSeverity.Error))
{
    throw new InvalidOperationException("Fix the reported issues before registering services.");
}

DecorationDiagnostics.Validate(typeof(MyService).Assembly);
```

### Common pitfalls

- Hosted services must implement `IHostedService` and any `BackgroundServiceAttribute.ServiceType` must be an interface implemented by the worker.
- Typed HTTP clients need a constructor that can accept `HttpClient`.
- HTTP handler types must inherit from `DelegatingHandler`.
- If you scan internal types, set `DecorationScanOptions.IncludeInternalTypes = true`.

## Analyzer support

The solution now includes `DiDecoration.Analyzers`, which provides compile-time feedback for invalid attribute usage:

- `DDI001` — invalid service mappings
- `DDI002` — invalid hosted-service usage
- `DDI003` — invalid typed HTTP client usage
- `DDI004` — invalid option attribute usage

If you package this library for distribution, include the analyzer project as an analyzer asset so consumer projects get the same feedback in the IDE.

