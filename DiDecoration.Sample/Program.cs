using DiDecoration.Generated;
using DiDecoration.Sample;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["SampleOptions:Greeting"] = "Configured greeting from the sample app";
builder.Services.RegisterDecoratorsGenerated(builder.Configuration);

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

