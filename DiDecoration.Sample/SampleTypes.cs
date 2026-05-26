using DiDecoration.Attributes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiDecoration.Sample;

public interface IWelcomeMessageService
{
    string GetMessage();
}

[SingletonService(typeof(IWelcomeMessageService))]
public sealed class WelcomeMessageService : IWelcomeMessageService
{
    public string GetMessage() => "Hello from the DiDecoration sample app.";
}

[BackgroundService]
public sealed class SampleWorker : BackgroundService
{
    private readonly IWelcomeMessageService _welcomeMessageService;
    private readonly ILogger<SampleWorker> _logger;

    public SampleWorker(IWelcomeMessageService welcomeMessageService, ILogger<SampleWorker> logger)
    {
        _welcomeMessageService = welcomeMessageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("{Message}", _welcomeMessageService.GetMessage());

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

[HttpClientService("https://api.example.com", 15, ClientName = "sample-api", DefaultHeaders = ["X-App=DiDecoration.Sample"])]
public sealed class SampleApiClient
{
    public SampleApiClient(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    public HttpClient HttpClient { get; }
}

[Option("SampleOptions")]
public sealed class SampleOptions
{
    public string Greeting { get; set; } = "Hello";
}

