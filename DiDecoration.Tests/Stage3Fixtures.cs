using Microsoft.Extensions.Hosting;

namespace DiDecoration.Tests;

public interface IInvalidStage3Service
{
}

public interface IInvalidStage3HostedService
{
}

public sealed class Stage3NotAHandler
{
}

public abstract class Stage3HostedServiceBase : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}






