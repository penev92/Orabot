using System;
using System.Threading;
using System.Threading.Tasks;
using Orabot.Bot.Discord;
using Xunit;

namespace Orabot.Tests.Discord
{
	public class DiscordReadinessTests
	{
		[Fact]
		public async Task WaitsForReadyAndReopensAfterReconnect()
		{
			var readiness = new DiscordReadiness();
			var initial = readiness.WaitUntilReadyAsync(CancellationToken.None);
			Assert.False(initial.IsCompleted);

			readiness.MarkReady();
			await initial.WaitAsync(TimeSpan.FromSeconds(5));
			await readiness.WaitUntilReadyAsync(CancellationToken.None);

			readiness.MarkDisconnected();
			var reconnect = readiness.WaitUntilReadyAsync(CancellationToken.None);
			Assert.False(reconnect.IsCompleted);
			readiness.MarkReady();
			await reconnect.WaitAsync(TimeSpan.FromSeconds(5));
		}

		[Fact]
		public async Task RepeatedDisconnectsDoNotOrphanExistingWaiters()
		{
			var readiness = new DiscordReadiness();
			var wait = readiness.WaitUntilReadyAsync(CancellationToken.None);
			readiness.MarkDisconnected();
			readiness.MarkDisconnected();
			readiness.MarkReady();
			readiness.MarkReady();
			await wait.WaitAsync(TimeSpan.FromSeconds(5));
		}

		[Fact]
		public async Task CancellingOneWaiterDoesNotCancelSharedReadiness()
		{
			var readiness = new DiscordReadiness();
			using var cancellation = new CancellationTokenSource();
			var cancelled = readiness.WaitUntilReadyAsync(cancellation.Token);
			var other = readiness.WaitUntilReadyAsync(CancellationToken.None);
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
			Assert.False(other.IsCompleted);
			readiness.MarkReady();
			await other.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}
}
