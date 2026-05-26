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

To enable it in a project, reference the generator as an analyzer:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>obj\Generated</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
<ItemGroup>
    <ProjectReference Include="..\DiDecoration.Generators\DiDecoration.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

The generator currently produces these methods in `DiDecoration.Generated.DecorationRegistrationExtensions`:

- `RegisterServicesGenerated(...)`
- `RegisterHostedServicesGenerated(...)`
- `RegisterHttpClientsGenerated(...)`
- `RegisterOptionsGenerated(...)`
- `RegisterDecoratorsGenerated(...)`

Use them with the same `IServiceCollection` and `IConfiguration` objects you already pass to the runtime API. Runtime scanning in `DiDecoration.Extensions.ServiceCollectionExtensions` remains the default fallback when you do not reference the generator package, or when you prefer dynamic assembly scanning.

```csharp
using DiDecoration.Generated;

builder.Services.RegisterDecoratorsGenerated(builder.Configuration);
```

## Packaging and deployment

The solution is structured so each shipable component can be packed independently:

- `DiDecoration` — runtime helpers for service registration, hosted services, HTTP clients, and options binding
- `DiDecoration.Analyzers` — compile-time diagnostics for invalid attribute usage
- `DiDecoration.Generators` — source generator that emits reflection-free registration helpers

The sample project in `DiDecoration.Sample` is reference-only and is not intended to be packed for consumers.

### Suggested deployment flow

1. Pack the runtime library and the analyzer/generator packages separately.
2. Publish the analyzer and generator packages as build-time dependencies for consumers.
3. Keep the runtime package available for reflection-based scanning and fallback scenarios.
4. Link release notes from `docs/releases/` when you publish a new version.

### Typical consumer setup

```xml
<ItemGroup>
  <PackageReference Include="DiDecoration" Version="x.y.z" />
  <PackageReference Include="DiDecoration.Analyzers" Version="x.y.z" PrivateAssets="all" />
  <PackageReference Include="DiDecoration.Generators" Version="x.y.z" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

### Source-generation sample

Use the generated helpers in any ASP.NET Core app after adding the analyzer reference shown above:

```csharp
using DiDecoration.Generated;

var builder = WebApplication.CreateBuilder(args);

builder.Services.RegisterDecoratorsGenerated(builder.Configuration);

var app = builder.Build();
app.MapGet("/sample", () => "Generated helpers are wired up.");
app.Run();
```

## Sample app

The solution includes a runnable ASP.NET Core sample in `DiDecoration.Sample`.
It demonstrates a complete generated-source startup path, including:

- service registration
- hosted-service registration
- typed HTTP clients
- configuration-bound options

The sample project references `DiDecoration.Generators` as an analyzer and writes emitted files to `DiDecoration.Sample/obj/Generated` so you can inspect the generated helper output during local development.

Run it with:

```powershell
dotnet run --project .\DiDecoration.Sample\DiDecoration.Sample.csproj
```

## Release notes

Versioned release notes live under `docs/releases/`.

- [`docs/releases/README.md`](docs/releases/README.md)
- [`docs/releases/v1.1.0.md`](docs/releases/v1.1.0.md)

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
- `ServiceAttribute.Key` enables keyed registrations when you need multiple implementations of the same service type.
- Set `ServiceAttribute.Multiple = true` when you want additional implementations to remain in the collection.
- `RegisterHostedServices` can resolve a hosted service directly or through another registered service type.
- `BackgroundServiceAttribute.Key` lets a hosted service resolve a keyed registration, and hosted-service lifetime checks fail fast if a worker is not singleton.
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

