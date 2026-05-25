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

## Source generation

If you reference the separate `DiDecoration.Generators` project/package, the compiler can emit reflection-free registration helpers directly into your app assembly.

The generator currently produces these methods in `DiDecoration.Generated.DecorationRegistrationExtensions`:

- `RegisterServicesGenerated(...)`
- `RegisterHostedServicesGenerated(...)`
- `RegisterHttpClientsGenerated(...)`
- `RegisterOptionsGenerated(...)`
- `RegisterDecoratorsGenerated(...)`

Use them with the same `IServiceCollection` and `IConfiguration` objects you already pass to the runtime API. Runtime scanning in `DiDecoration.Extensions.ServiceCollectionExtensions` remains the default fallback when you do not reference the generator package, or when you prefer dynamic assembly scanning.

```csharp
using DiDecoration.Generated;

services.RegisterDecoratorsGenerated(configuration);
```

## Advanced examples

### 1) Register a focused slice of an assembly

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

### 2) Keep registration order predictable

When a hosted service resolves another registered service type, register the dependency in the same scan set and keep both calls in the startup pipeline:

```csharp
services
    .RegisterServices(typeof(WorkerDependencies).Assembly)
    .RegisterHostedServices(typeof(WorkerDependencies).Assembly);
```

### 3) Use diagnostics before registering

```csharp
var diagnostics = DecorationDiagnostics.Analyze(typeof(MyFeatureMarker).Assembly);

if (diagnostics.Any(item => item.Severity == DecorationDiagnosticSeverity.Error))
{
    throw new InvalidOperationException("Fix attribute usage before continuing.");
}
```

### 4) Use the analyzer for compile-time feedback

If your solution references `DiDecoration.Analyzers`, invalid attribute usage shows up directly in the editor and build output. That is the fastest way to catch problems such as:

- mapping a service to an interface it does not implement
- assigning `BackgroundServiceAttribute` to a type that is not an `IHostedService`
- giving `HttpClientServiceAttribute` an invalid URL or handler type
- leaving an `[Option]` key empty

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
- Use `DecorationDiagnostics.Validate(...)` when you want to fail fast with a list of all discovered attribute issues.

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
- If you use the aggregate registration helper, remember that `RegisterDecorators(...)` will run services, hosted services, HTTP clients, and options in one pass.
- If you reference `DiDecoration.Generators`, prefer the generated helpers for startup performance; keep the runtime `RegisterDecorators(...)` path when you need reflection-based scanning or a fallback for dynamic assemblies.
- If a type is intentionally excluded by namespace or predicate filters, it will not be registered even if it has valid attributes.

## Analyzer support

The solution now includes `DiDecoration.Analyzers`, which provides compile-time feedback for invalid attribute usage:

- `DDI001` — invalid service mappings
- `DDI002` — invalid hosted-service usage
- `DDI003` — invalid typed HTTP client usage
- `DDI004` — invalid option attribute usage

If you package this library for distribution, include the analyzer project as an analyzer asset so consumer projects get the same feedback in the IDE.

## Troubleshooting checklist

If registration behaves unexpectedly, check these common causes first:

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| Service not resolved | The type was filtered out by `DecorationScanOptions` | Relax the namespace/predicate filter or include the target namespace |
| Hosted service fails at startup | A dependency service was not registered | Register the dependency in the same scan set or before `RegisterHostedServices(...)` |
| Typed client throws on build | `BaseUrl` is invalid or the handler type is wrong | Use an absolute URI and ensure handlers inherit from `DelegatingHandler` |
| Options are empty | The key does not match the configuration section | Confirm the `[Option("Key")]` value matches the config path |
| Analyzer reports an attribute error | The attribute usage is invalid | Follow the diagnostic message; the analyzer IDs are listed above |

When in doubt, run the diagnostics helper before registration:

```csharp
var diagnostics = DecorationDiagnostics.Validate(typeof(MyFeatureMarker).Assembly);
```

That gives you a single place to review all discovered issues before they turn into runtime surprises.

