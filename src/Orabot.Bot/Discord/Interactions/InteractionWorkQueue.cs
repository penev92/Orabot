using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Orabot.Bot.Infrastructure.BackgroundWork;

namespace Orabot.Bot.Discord.Interactions
{
	/// <summary>Owns bounded interaction work so SDK execution and its dependency scope remain awaited.</summary>
	internal sealed class InteractionWorkQueue : SupervisedBackgroundService
	{
		private readonly Channel<Func<CancellationToken, Task>> _work;
		private readonly int _workerCount;
		private readonly ILogger<InteractionWorkQueue> _logger;

		public InteractionWorkQueue(ILogger<InteractionWorkQueue> logger, WorkerFailureState failureState,
			int capacity = 64, int workerCount = 4) : base(logger, failureState)
		{
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(workerCount);
			_logger = logger;
			_workerCount = workerCount;
			_work = Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(capacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				AllowSynchronousContinuations = false
			});
		}

		public bool TryEnqueue(Func<CancellationToken, Task> work)
		{
			ArgumentNullException.ThrowIfNull(work);
			return _work.Writer.TryWrite(work);
		}

		protected override Task ExecuteSupervisedAsync(CancellationToken stoppingToken) =>
			Task.WhenAll(Enumerable.Range(0, _workerCount).Select(_ => ConsumeAsync(stoppingToken)));

		private async Task ConsumeAsync(CancellationToken stoppingToken)
		{
			// Stop completes the writer. Drain admitted callbacks with cancellation so each can
			// complete its deferred response instead of silently abandoning queued interactions.
			await foreach (var work in _work.Reader.ReadAllAsync())
			{
				try
				{
					await work(stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
				catch (Exception exception)
				{
					_logger.LogWarning("Interaction work failed ({ExceptionType}).", exception.GetType().FullName);
				}
			}
		}

		public override Task StopAsync(CancellationToken cancellationToken)
		{
			_work.Writer.TryComplete();
			return base.StopAsync(cancellationToken);
		}

		public override void Dispose()
		{
			_work.Writer.TryComplete();
			base.Dispose();
		}
	}
}
