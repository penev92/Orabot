using Orabot.Bot.Discord.BackgroundWork;
using Microsoft.Extensions.Logging.Abstractions;
using Orabot.Bot.Discord.Interactions;
using Orabot.Bot.Infrastructure.BackgroundWork;

namespace Orabot.Tests.Interactions;

/// <summary>Checks bounded admission, concurrency, cancellation and fault isolation without a Gateway or timer sleeps.</summary>
public sealed class BotWorkQueueTests
{
	[Fact]
	public async Task Rejects_work_when_the_bounded_queue_is_full()
	{
		var failure = new WorkerFailureState();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, failure, capacity: 1, workerCount: 1);
		var executed = Signal();
		Assert.True(queue.TryEnqueue(_ => { executed.TrySetResult(); return Task.CompletedTask; }));
		Assert.False(queue.TryEnqueue(_ => throw new InvalidOperationException("Rejected work must never execute.")));
		await queue.StartAsync(CancellationToken.None);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(failure.HasFailed);
	}

	[Fact]
	public async Task Executes_only_the_configured_number_of_jobs_concurrently()
	{
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, new WorkerFailureState(), capacity: 4, workerCount: 2);
		var bothStarted = Signal();
		var release = Signal();
		var allCompleted = Signal();
		int started = 0, running = 0, maximum = 0, completed = 0;
		for (var index = 0; index < 4; index++)
			Assert.True(queue.TryEnqueue(async token =>
			{
				var active = Interlocked.Increment(ref running);
				int previous;
				do { previous = Volatile.Read(ref maximum); }
				while (previous < active && Interlocked.CompareExchange(ref maximum, active, previous) != previous);
				if (Interlocked.Increment(ref started) == 2) bothStarted.TrySetResult();
				try { await release.Task.WaitAsync(token); }
				finally
				{
					Interlocked.Decrement(ref running);
					if (Interlocked.Increment(ref completed) == 4) allCompleted.TrySetResult();
				}
			}));
		await queue.StartAsync(CancellationToken.None);
		await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(2, Volatile.Read(ref started));
		release.TrySetResult();
		await allCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(2, maximum);
	}

	[Fact]
	public async Task Stop_cancels_active_work_and_drains_admitted_work_with_cancellation()
	{
		var failure = new WorkerFailureState();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, failure, capacity: 2, workerCount: 1);
		var started = Signal();
		var drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.True(queue.TryEnqueue(async token =>
		{
			started.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
		}));
		Assert.True(queue.TryEnqueue(token => { drained.TrySetResult(token.IsCancellationRequested); return Task.CompletedTask; }));
		await queue.StartAsync(CancellationToken.None);
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(await drained.Task);
		Assert.False(queue.TryEnqueue(_ => Task.CompletedTask));
		Assert.False(failure.HasFailed);
	}

	[Fact]
	public async Task One_failed_job_does_not_stop_following_jobs()
	{
		var failure = new WorkerFailureState();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, failure, capacity: 2, workerCount: 1);
		var completed = Signal();
		Assert.True(queue.TryEnqueue(_ => throw new InvalidOperationException("synthetic-private-payload")));
		Assert.True(queue.TryEnqueue(_ => { completed.TrySetResult(); return Task.CompletedTask; }));
		await queue.StartAsync(CancellationToken.None);
		await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(failure.HasFailed);
	}

	static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
