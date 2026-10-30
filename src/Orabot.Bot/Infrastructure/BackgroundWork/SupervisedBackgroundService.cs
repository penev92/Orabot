using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Infrastructure.BackgroundWork
{
	public abstract class SupervisedBackgroundService : BackgroundService
	{
		private readonly ILogger _logger;
		private readonly WorkerFailureState _failureState;

		protected SupervisedBackgroundService(ILogger logger, WorkerFailureState failureState)
		{
			_logger = logger;
			_failureState = failureState;
		}

		protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				await ExecuteSupervisedAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// Cancellation is the normal hosted-service shutdown path.
			}
			catch (Exception exception)
			{
				_failureState.MarkFailed();
				_logger.LogCritical("Background worker {WorkerName} terminated unexpectedly; exception type: {ExceptionType}",
					GetType().Name, exception.GetType().FullName);
				// Host also logs the escaped exception. Avoid forwarding remote payloads or credentials
				// through the original message, stack trace or inner exception.
				throw new InvalidOperationException($"Background worker {GetType().Name} failed with {exception.GetType().FullName}.");
			}
		}

		protected abstract Task ExecuteSupervisedAsync(CancellationToken stoppingToken);
	}
}
