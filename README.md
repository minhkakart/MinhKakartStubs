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

[HttpClientService("https://api.example.com", 30)]
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
- `RegisterOptions` binds each `[Option]` class from the configuration section named by its key.

