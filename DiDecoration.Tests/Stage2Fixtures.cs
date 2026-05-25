using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;

namespace DiDecoration.Tests.Stage2.Filtering
{
    [TransientService]
    public sealed class ScanNamespaceVisibleService
    {
    }

    [TransientService]
    public sealed class ScanNamespaceHiddenService
    {
    }

    [TransientService]
    internal sealed class ScanNamespaceInternalService
    {
    }
}

namespace DiDecoration.Tests.Stage2.Generic
{
    public interface IOpenGenericRepository<T>
    {
        T? Create();
    }

    [SingletonService(typeof(IOpenGenericRepository<>))]
    public sealed class OpenGenericRepository<T> : IOpenGenericRepository<T>
    {
        public T? Create() => default;
    }
}

namespace DiDecoration.Tests.Stage2.Http
{
    [HttpClientService("https://api.example.com", 20, typeof(NotAHandler), ClientName = "custom-client", DefaultHeaders = new[] { "X-App=Stage2" }, Handlers = new[] { typeof(RecordingHandler) })]
    public sealed class EnhancedCatalogClient
    {
        public EnhancedCatalogClient(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public HttpClient HttpClient { get; }
    }

    public sealed class RecordingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    public sealed class NotAHandler
    {
    }
}

namespace DiDecoration.Tests.Stage2.Aggregate
{
    public interface IAggregateWorker
    {
    }

    [SingletonService(typeof(IAggregateWorker))]
    [BackgroundService(typeof(IAggregateWorker))]
    public sealed class AggregateWorker : HostedServiceBase, IAggregateWorker
    {
    }

    [HttpClientService("https://api.example.com", 10, typeof(RecordingHandler), DefaultHeaders = new[] { "X-Agg=Yes" })]
    public sealed class AggregateClient
    {
        public AggregateClient(HttpClient httpClient)
        {
            HttpClient = httpClient;
        }

        public HttpClient HttpClient { get; }
    }

    [Option("AggregateOptions")]
    public sealed class AggregateOptions
    {
        public string? Label { get; set; }
    }

    public abstract class HostedServiceBase : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class RecordingHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}



