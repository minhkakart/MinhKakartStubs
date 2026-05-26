using DiDecoration.Extensions;
using DiDecoration.Sample;
using DiDecoration.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["SampleOptions:Greeting"] = "Configured greeting from the sample app";

builder.Services.RegisterDecorators(
    builder.Configuration,
    typeof(WelcomeMessageService).Assembly,
    new DecorationScanOptions
    {
        NamespacePrefix = typeof(WelcomeMessageService).Namespace,
        IncludeInternalTypes = true
    });

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Name = "DiDecoration sample app",
    ReleaseNotes = "/docs/releases",
    Sample = "/sample"
}));

app.MapGet("/sample", (IWelcomeMessageService messages, SampleApiClient client, IOptions<SampleOptions> options) => Results.Ok(new
{
    ServiceMessage = messages.GetMessage(),
    OptionsGreeting = options.Value.Greeting,
    HttpClientBaseAddress = client.HttpClient.BaseAddress?.ToString(),
    HttpClientTimeoutSeconds = client.HttpClient.Timeout.TotalSeconds
}));

app.MapGet("/sample/hosted-services", (IEnumerable<IHostedService> hostedServices) => Results.Ok(new
{
    HostedServices = hostedServices.Select(service => service.GetType().Name).ToArray()
}));

app.Run();

