using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orabot.Bot.DependencyInjection;
using Orabot.Hosts.ConsoleHost.CommandDeployment;
using Orabot.Bot.Infrastructure.BackgroundWork;

namespace Orabot.Hosts.ConsoleHost
{
	internal class Program
	{
		static async Task<int> Main(string[] args)
		{
			try
			{
				if (args.Length > 0 && string.Equals(args[0], "commands", StringComparison.OrdinalIgnoreCase))
				{
					if (!CommandDeploymentArguments.TryParse(args, out var request, out var error))
					{
						Console.Error.WriteLine(error);
						Console.Error.WriteLine(CommandDeploymentRunner.Usage);
						return 2;
					}
					using var cancellation = new CancellationTokenSource();
					ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
					Console.CancelKeyPress += cancel;
					try { return await CommandDeploymentRunner.RunAsync(request, Console.Out, Console.Error, cancellation.Token); }
					catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
					{
						Console.Error.WriteLine("Command operation cancelled.");
						return 130;
					}
					finally { Console.CancelKeyPress -= cancel; }
				}
				if (args.Length == 1 && args[0] is "--help" or "-h")
				{
					Console.WriteLine(CommandDeploymentRunner.Usage);
					return 0;
				}
				var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
				{
					Args = args,
					ContentRootPath = AppContext.BaseDirectory
				});
				OrabotConfiguration.Configure(builder, args);
				builder.Logging.ClearProviders().AddSimpleConsole(options =>
				{
					options.SingleLine = true;
					options.UseUtcTimestamp = true;
					options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z' ";
				});
				builder.Services.Configure<HostOptions>(options =>
				{
					options.ShutdownTimeout = TimeSpan.FromSeconds(30);
					options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
				});
				builder.Services.AddOrabot(builder.Configuration);
				using var host = builder.Build();
				OrabotConfiguration.Validate(host.Services);
				var failureState = host.Services.GetRequiredService<WorkerFailureState>();
				await host.RunAsync();
				return failureState.HasFailed ? 1 : 0;
			}
			catch (OptionsValidationException exception)
			{
				foreach (var failure in exception.Failures)
					Console.Error.WriteLine(failure);
				return 1;
			}
			catch (Exception exception)
			{
				// Startup exceptions can contain configuration or transport values.
				Console.Error.WriteLine($"Orabot could not complete its host lifetime ({exception.GetType().Name}).");
				return 1;
			}
		}
	}
}
