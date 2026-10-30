using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orabot.Bot.Discord;
using Orabot.Bot.Discord.Commands;
using Orabot.Bot.Discord.Interactions;
using Orabot.Bot.Discord.Logging;
using Orabot.Bot.Discord.Messages;
using Xunit;
using DiscordBot = Orabot.Bot.Discord.Bot;

namespace Orabot.Tests.Discord
{
	public class BotLifecycleTests
	{
		[Fact]
		public async Task StartsOnceAndStopsBeforeLoggingOut()
		{
			using var fixture = new Fixture();
			await fixture.Bot.StartAsync(CancellationToken.None);
			await fixture.Bot.StopAsync(CancellationToken.None);
			await fixture.Bot.StopAsync(CancellationToken.None);
			Assert.Equal(new[] { "login", "start", "stop", "logout" }, fixture.Connection.Calls);
			Assert.NotEmpty(fixture.Commands.Search("probe").Commands);
			await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Bot.StartAsync(CancellationToken.None));
		}

		[Fact]
		public async Task CancelledStartupDoesNotOpenConnection()
		{
			using var fixture = new Fixture();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Bot.StartAsync(new CancellationToken(true)));
			Assert.Empty(fixture.Connection.Calls);
		}

		[Fact]
		public async Task CancellationAfterLoginStillCleansUpConnection()
		{
			using var fixture = new Fixture();
			using var cancellation = new CancellationTokenSource();
			fixture.Connection.AfterLogin = cancellation.Cancel;
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Bot.StartAsync(cancellation.Token));
			Assert.Equal(new[] { "login", "stop", "logout" }, fixture.Connection.Calls);
		}

		[Fact]
		public async Task FailedStartupIsCleanedUpAndOnlySafeFailureMetadataEscapes()
		{
			using var fixture = new Fixture();
			var expected = new InvalidOperationException("synthetic-private-startup-payload", new Exception("synthetic-token"));
			fixture.Connection.StartFailure = expected;
			var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Bot.StartAsync(CancellationToken.None));
			Assert.NotSame(expected, failure);
			Assert.Contains(expected.GetType().FullName!, failure.Message);
			Assert.Null(failure.InnerException);
			Assert.DoesNotContain("synthetic-private-startup-payload", failure.ToString());
			Assert.DoesNotContain("synthetic-token", failure.ToString());
			Assert.Equal(new[] { "login", "start", "stop", "logout" }, fixture.Connection.Calls);
			Assert.False(fixture.Readiness.WaitUntilReadyAsync(CancellationToken.None).IsCompleted);
		}

		[Fact]
		public async Task LogoutStillRunsWhenStopFails()
		{
			using var fixture = new Fixture();
			await fixture.Bot.StartAsync(CancellationToken.None);
			fixture.Connection.StopFailure = new InvalidOperationException("synthetic-private-shutdown-payload", new Exception("synthetic-token"));
			var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Bot.StopAsync(CancellationToken.None));
			Assert.Contains(fixture.Connection.StopFailure.GetType().FullName!, failure.Message);
			Assert.Null(failure.InnerException);
			Assert.DoesNotContain("synthetic-private-shutdown-payload", failure.ToString());
			Assert.DoesNotContain("synthetic-token", failure.ToString());
			Assert.Equal(new[] { "login", "start", "stop", "logout" }, fixture.Connection.Calls);
		}

		[Fact]
		public async Task UnexpectedSdkCancellationIsSanitizedWhenHostWasNotCancelled()
		{
			using var fixture = new Fixture();
			fixture.Connection.StartFailure = new OperationCanceledException("synthetic-private-sdk-payload");
			var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Bot.StartAsync(CancellationToken.None));
			Assert.Contains(typeof(OperationCanceledException).FullName!, failure.Message);
			Assert.Null(failure.InnerException);
			Assert.DoesNotContain("synthetic-private-sdk-payload", failure.ToString());
			Assert.Equal(new[] { "login", "start", "stop", "logout" }, fixture.Connection.Calls);
		}

		[Fact]
		public async Task ResumedConnectionReleasesWorkersWithoutAnotherReadyEvent()
		{
			using var fixture = new Fixture();
			await fixture.Bot.OnConnectedAsync();
			Assert.False(fixture.Readiness.WaitUntilReadyAsync(CancellationToken.None).IsCompleted);
			await fixture.Bot.OnReadyAsync();
			await fixture.Bot.OnReadyAsync();
			await fixture.Readiness.WaitUntilReadyAsync(CancellationToken.None);
			await fixture.Bot.OnDisconnectedAsync(new Exception("Synthetic disconnect."));
			var reconnect = fixture.Readiness.WaitUntilReadyAsync(CancellationToken.None);
			Assert.False(reconnect.IsCompleted);
			await fixture.Bot.OnConnectedAsync();
			await reconnect.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.Empty(fixture.Connection.Calls);
		}

		[Fact]
		public async Task LateGatewayEventsCannotReopenReadinessDuringShutdown()
		{
			using var fixture = new Fixture();
			await fixture.Bot.StartAsync(CancellationToken.None);
			await fixture.Bot.OnReadyAsync();
			await fixture.Bot.StopAsync(new CancellationToken(true));
			await fixture.Bot.OnConnectedAsync();
			await fixture.Bot.OnReadyAsync();
			Assert.False(fixture.Readiness.WaitUntilReadyAsync(CancellationToken.None).IsCompleted);
			Assert.Equal(new[] { "login", "start", "stop", "logout" }, fixture.Connection.Calls);
		}

		private sealed class Fixture : IDisposable
		{
			private readonly DiscordSocketClient _client = new();
			public CommandService Commands { get; } = new();
			public FakeConnection Connection { get; } = new();
			public DiscordReadiness Readiness { get; } = new();
			public DiscordBot Bot { get; }

			public Fixture()
			{
				var handlers = new NoOpHandlers();
				Bot = new DiscordBot(_client, Commands, new EmptyServices(), Array.Empty<BaseTypeReader>(),
					new ModuleBase<SocketCommandContext>[] { new LifecycleProbeModule() }, handlers, handlers, handlers, handlers, handlers,
					Readiness, Options.Create(new DiscordBotOptions { BotToken = "synthetic-test-token" }), NullLogger<DiscordBot>.Instance, Connection);
			}

			public void Dispose()
			{
				((IDisposable)Commands).Dispose();
				_client.Dispose();
			}
		}

		private sealed class EmptyServices : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}

		private sealed class FakeConnection : IDiscordConnectionLifecycle
		{
			public List<string> Calls { get; } = new();
			public Exception? StartFailure { get; set; }
			public Exception? StopFailure { get; set; }
			public Action? AfterLogin { get; set; }
			public Task LoginAsync(string botToken) { Calls.Add("login"); AfterLogin?.Invoke(); return Task.CompletedTask; }
			public Task StartAsync() { Calls.Add("start"); return StartFailure == null ? Task.CompletedTask : Task.FromException(StartFailure); }
			public Task StopAsync() { Calls.Add("stop"); return StopFailure == null ? Task.CompletedTask : Task.FromException(StopFailure); }
			public Task LogoutAsync() { Calls.Add("logout"); return Task.CompletedTask; }
		}

		private sealed class NoOpHandlers : ILogEventHandler, IMessageEventHandler, IReactionEventHandler, ISlashCommandEventHandler, IAutocompleteEventHandler
		{
			public Task Log(LogMessage message) => Task.CompletedTask;
			public Task HandleMessageReceivedAsync(SocketMessage message) => Task.CompletedTask;
			public Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction) => Task.CompletedTask;
			public Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction) => Task.CompletedTask;
			public Task HandleSlashCommandAsync(SocketSlashCommand command) => Task.CompletedTask;
			public Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction) => Task.CompletedTask;
		}
	}

	public class LifecycleProbeModule : ModuleBase<SocketCommandContext>
	{
		[Command("probe")]
		public Task Probe() => Task.CompletedTask;
	}
}
