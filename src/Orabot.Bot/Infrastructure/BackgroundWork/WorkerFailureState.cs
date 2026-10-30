using System.Threading;

namespace Orabot.Bot.Infrastructure.BackgroundWork
{
	public sealed class WorkerFailureState
	{
		private int _hasFailed;

		public bool HasFailed => Volatile.Read(ref _hasFailed) != 0;

		internal void MarkFailed() => Interlocked.Exchange(ref _hasFailed, 1);
	}
}
