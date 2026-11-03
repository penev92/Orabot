using System.Collections.Concurrent;
using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orabot.Bot.Discord.BackgroundWork;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Orabot.Tests.Interactions;
using Orabot.Tests.TestDoubles;

namespace Orabot.Tests.Messages;

public sealed class MessageEventHandlerTests
{
	[Fact]
	public async Task Gateway_admission_returns_while_matching_handlers_are_awaited_in_order()
	{
		var entered = Signal();
		var release = Signal();
		var finished = Signal();
		var order = new List<int>();
		var unmatched = new Handler(_ => throw new InvalidOperationException("An unmatched handler must not run."), matches: false);
		var first = new Handler(async token =>
		{
			entered.TrySetResult();
			await release.Task.WaitAsync(token);
			order.Add(1);
		});
		var last = new Handler(_ => { order.Add(2); finished.TrySetResult(); return Task.CompletedTask; });
		await using var fixture = new Fixture(new[] { unmatched, first, last });
		await fixture.Queue.StartAsync(CancellationToken.None);

		var admission = fixture.Dispatcher.HandleAsync(Message());
		Assert.True(admission.IsCompletedSuccessfully);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(finished.Task.IsCompleted);
		release.TrySetResult();
		await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.StopAsync();
		Assert.Equal(new[] { 1, 2 }, order);
		Assert.Equal(0, unmatched.Invocations);
	}

	[Fact]
	public async Task Matching_or_execution_failure_is_sanitized_and_does_not_skip_the_next_handler()
	{
		const string privateValue = "synthetic-private-handler-message";
		const string privateBody = "synthetic-private-user-content";
		var finished = Signal();
		var matchFailure = new Handler(_ => Task.CompletedTask, matchFailure: new InvalidOperationException(privateValue));
		var invokeFailure = new Handler(_ => throw new InvalidOperationException(privateValue, new Exception(privateValue)));
		var next = new Handler(_ => { finished.TrySetResult(); return Task.CompletedTask; });
		await using var fixture = new Fixture(new[] { matchFailure, invokeFailure, next });
		await fixture.Queue.StartAsync(CancellationToken.None);
		await fixture.Dispatcher.HandleAsync(Message(content: privateBody));
		await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.StopAsync();
		Assert.Equal(1, next.Invocations);
		Assert.Equal(2, fixture.Logs.Count);
		Assert.All(fixture.Logs, entry =>
		{
			Assert.Null(entry.Exception);
			Assert.Contains(typeof(InvalidOperationException).FullName!, entry.Message);
			Assert.DoesNotContain(privateValue, entry.Message);
			Assert.DoesNotContain(privateBody, entry.Message);
		});
		Assert.False(fixture.Failure.HasFailed);
	}

	[Theory]
	[InlineData(true, MessageSource.User)]
	[InlineData(false, MessageSource.System)]
	public async Task Bot_or_system_messages_are_not_admitted(bool bot, MessageSource source)
	{
		var handler = new Handler(_ => throw new InvalidOperationException("Ignored messages must not execute."));
		await using var fixture = new Fixture(new[] { handler }, capacity: 1);
		await fixture.Dispatcher.HandleAsync(Message(bot, source));
		Assert.True(fixture.Queue.TryEnqueue(_ => Task.CompletedTask));
		Assert.Equal(0, handler.Invocations);
	}

	[Fact]
	public async Task Full_queue_skips_message_work_without_waiting_for_capacity()
	{
		var handler = new Handler(_ => throw new InvalidOperationException("Rejected messages must not execute."));
		await using var fixture = new Fixture(new[] { handler }, capacity: 1);
		Assert.True(fixture.Queue.TryEnqueue(_ => Task.CompletedTask));
		var admission = fixture.Dispatcher.HandleAsync(Message());
		Assert.True(admission.IsCompletedSuccessfully);
		Assert.Equal(0, handler.Invocations);
		Assert.Single(fixture.Logs);
	}

	[Fact]
	public async Task Lifetime_cancellation_reaches_the_active_handler_and_owned_callback_finishes()
	{
		var entered = Signal();
		var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var active = new Handler(async token =>
		{
			entered.TrySetResult();
			try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
			finally { cancelled.TrySetResult(token.IsCancellationRequested); }
		});
		var later = new Handler(_ => throw new InvalidOperationException("Cancelled processing must stop dispatch."));
		await using var fixture = new Fixture(new[] { active, later });
		await fixture.Queue.StartAsync(CancellationToken.None);
		await fixture.Dispatcher.HandleAsync(Message());
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		fixture.Lifetime.StopApplication();
		Assert.True(await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5)));
		await fixture.StopAsync();
		Assert.Equal(0, later.Invocations);
		Assert.False(fixture.Failure.HasFailed);
	}

	static IUserMessage Message(bool bot = false, MessageSource source = MessageSource.User, string content = "hello")
	{
		var author = DiscordTestProxy.Create<IUser>((method, _) => method == "get_IsBot" ? bot : throw new NotSupportedException(method));
		return DiscordTestProxy.Create<IUserMessage>((method, _) => method switch
		{
			"get_Author" => author, "get_Source" => source, "get_Id" => (ulong)123, "get_Content" => content,
			_ => throw new NotSupportedException(method)
		});
	}

	static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	sealed class Handler(Func<CancellationToken, Task> invoke, bool matches = true, Exception? matchFailure = null) : ICustomMessageHandler
	{
		public int Invocations { get; private set; }
		public bool CanHandle(IUserMessage _) => matchFailure == null ? matches : throw matchFailure;
		public Task InvokeAsync(IUserMessage message, CancellationToken token = default) { Invocations++; return invoke(token); }
	}

	sealed record Entry(string Message, Exception? Exception);
	sealed class RecordingLogger(ConcurrentQueue<Entry> entries) : ILogger<MessageEventHandler>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel level) => true;
		public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			entries.Enqueue(new(formatter(state, exception), exception));
	}

	sealed class Fixture : IAsyncDisposable
	{
		public OfflineHostApplicationLifetime Lifetime { get; } = new();
		public WorkerFailureState Failure { get; } = new();
		public ConcurrentQueue<Entry> Logs { get; } = new();
		public BotWorkQueue Queue { get; }
		public MessageEventHandler Dispatcher { get; }
		public Fixture(IEnumerable<ICustomMessageHandler> handlers, int capacity = 8)
		{
			Queue = new(NullLogger<BotWorkQueue>.Instance, Failure, capacity, workerCount: 1);
			Dispatcher = new(handlers, Queue, Lifetime, new RecordingLogger(Logs));
		}
		public Task StopAsync() => Queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		public async ValueTask DisposeAsync()
		{
			Lifetime.StopApplication();
			try { await StopAsync(); }
			finally { Queue.Dispose(); Lifetime.Dispose(); }
		}
	}
}
