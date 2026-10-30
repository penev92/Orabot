using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Discord
{
	/// <summary>A reusable gate for initial readiness and subsequent connection interruptions.</summary>
	public sealed class DiscordReadiness
	{
		private readonly object _sync = new();
		private TaskCompletionSource<bool> _ready = CreateSignal();

		public Task WaitUntilReadyAsync(CancellationToken cancellationToken)
		{
			Task ready;
			lock (_sync)
				ready = _ready.Task;
			return ready.WaitAsync(cancellationToken);
		}

		public void MarkReady()
		{
			lock (_sync)
				_ready.TrySetResult(true);
		}

		public void MarkDisconnected()
		{
			lock (_sync)
			{
				// Preserve existing waiters when already disconnected rather than orphaning them.
				if (_ready.Task.IsCompleted)
					_ready = CreateSignal();
			}
		}

		private static TaskCompletionSource<bool> CreateSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
	}
}
