using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Orabot.Bot.Infrastructure.BackgroundWork;

namespace Orabot.Tests.Hosting;

public sealed class HostedWorkerFailureTests
{
    [Fact]
    public async Task Unexpected_worker_failure_stops_the_host_and_records_failed_exit_state()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        builder.Services.AddSingleton<WorkerFailureState>();
        builder.Services.AddSingleton<FailingWorker>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<FailingWorker>());
        using var host = builder.Build();
        await host.StartAsync();
        host.Services.GetRequiredService<FailingWorker>().Fail();

        await host.WaitForShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(host.Services.GetRequiredService<WorkerFailureState>().HasFailed);
        Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
    }

    sealed class FailingWorker : SupervisedBackgroundService
    {
        readonly TaskCompletionSource fail = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FailingWorker(WorkerFailureState failureState) : base(NullLogger.Instance, failureState) { }
        public void Fail() => fail.TrySetResult();
        protected override async Task ExecuteSupervisedAsync(CancellationToken stoppingToken)
        {
            await fail.Task.WaitAsync(stoppingToken);
            throw new InvalidOperationException("synthetic-private-payload");
        }
    }
}
