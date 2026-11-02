using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Autocomplete;
using Orabot.Bot.Features.Documentation.Commands;
using Orabot.Bot.Features.Documentation.Models;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Discord.Interactions;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Orabot.Tests.Documentation;

namespace Orabot.Tests.Interactions;

/// <summary>Exercises acknowledgment ownership, queued SDK execution, scoped lifetimes and safe failures without Discord traffic.</summary>
public sealed class InteractionDispatcherTests
{
	[Fact]
	public async Task Public_acknowledgment_finishes_before_execution_and_scope_lives_until_awaited_completion()
	{
		await using var fixture = await Fixture.CreateAsync();
		var acknowledgmentEntered = Signal();
		var allowAcknowledgment = Signal();
		var interaction = new OfflineSlashCommandInteraction("dispatcher-probe")
		{
			DeferHandler = async (ephemeral, options) =>
			{
				Assert.False(ephemeral);
				acknowledgmentEntered.TrySetResult();
				await allowAcknowledgment.Task.WaitAsync(options.CancelToken);
			}
		};

		var dispatch = fixture.HandleAsync(interaction);
		await acknowledgmentEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.False(fixture.State.Entered.Task.IsCompleted);
		Assert.Equal(0, fixture.State.ScopesCreated);
		allowAcknowledgment.TrySetResult();
		await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.True(fixture.State.AcknowledgedAtExecution);
		Assert.Equal(1, fixture.State.Executions);
		Assert.Equal(1, fixture.State.ScopesCreated);
		Assert.NotNull(fixture.State.ActiveScope);
		Assert.False(fixture.State.ActiveScope.IsDisposed);
		Assert.Empty(interaction.ResponseEdits);

		fixture.State.AllowCompletion.TrySetResult();
		var response = await interaction.FirstEdit.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.State.ScopeDisposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.DrainAsync();

		Assert.True(fixture.State.ScopeAliveBeforeResponse);
		Assert.True(fixture.State.ActiveScope.IsDisposed);
		Assert.Equal(1, fixture.State.ScopesDisposed);
		Assert.Equal("probe-completed", response.Content.Value);
		Assert.Equal(new[] { "defer:public", "modify" }, interaction.Calls);
		Assert.Equal(1, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(1, interaction.ModifyCount);
		Assert.Single(interaction.ResponseEdits);
		Assert.Empty(interaction.Followups);
	}

	[Fact]
	public async Task Module_failure_edits_the_original_response_and_logs_no_private_exception_payload()
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineSlashCommandInteraction("dispatcher-fault");

		await fixture.HandleAsync(interaction);
		var response = await interaction.FirstEdit.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.DrainAsync();

		Assert.Equal(1, fixture.State.Executions);
		Assert.Equal("The command could not be completed. Please try again later.", response.Content.Value);
		Assert.Empty(response.Embeds.Value);
		Assert.Equal(new[] { "defer:public", "modify" }, interaction.Calls);
		Assert.Empty(interaction.InitialResponses);
		Assert.Empty(interaction.Followups);
		Assert.NotEmpty(fixture.Logs);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Unknown_command_gets_one_safe_completion_without_running_a_module()
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineSlashCommandInteraction("missing-command");

		await fixture.HandleAsync(interaction);
		var response = await interaction.FirstEdit.WaitAsync(TimeSpan.FromSeconds(5));
		await fixture.DrainAsync();

		Assert.Equal("This command is not available in this version of Orabot.", response.Content.Value);
		Assert.Equal(0, fixture.State.Executions);
		Assert.Equal(new[] { "defer:public", "modify" }, interaction.Calls);
		Assert.Equal(1, interaction.ModifyCount);
		Assert.Empty(interaction.Followups);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Full_queue_replies_ephemerally_without_deferral_or_execution()
	{
		await using var fixture = await Fixture.CreateAsync(capacity: 1);
		var active = Signal();
		var releaseActive = Signal();
		var pendingCompleted = Signal();
		Assert.True(fixture.Queue.TryEnqueue(async token =>
		{
			active.TrySetResult();
			await releaseActive.Task.WaitAsync(token);
		}));
		await active.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(fixture.Queue.TryEnqueue(_ =>
		{
			pendingCompleted.TrySetResult();
			return Task.CompletedTask;
		}));
		var interaction = new OfflineSlashCommandInteraction("dispatcher-probe");

		await fixture.HandleAsync(interaction);

		Assert.Equal(new[] { "respond:ephemeral" }, interaction.Calls);
		Assert.Equal("Orabot is busy. Please try again shortly.", Assert.Single(interaction.InitialResponses).Content.Value);
		Assert.Equal(0, interaction.DeferCount);
		Assert.Equal(0, interaction.ModifyCount);
		Assert.Equal(0, fixture.State.Executions);
		releaseActive.TrySetResult();
		await pendingCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task Failed_acknowledgment_is_observed_without_execution_or_a_second_initial_response()
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineSlashCommandInteraction("dispatcher-probe")
		{
			DeferHandler = (_, _) => Task.FromException(new InvalidOperationException(DispatcherProbeState.PrivatePayload))
		};

		await fixture.HandleAsync(interaction);
		await fixture.DrainAsync();

		Assert.Equal(new[] { "defer:public" }, interaction.Calls);
		Assert.Equal(1, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(0, interaction.ModifyCount);
		Assert.False(interaction.HasResponded);
		Assert.Empty(interaction.ResponseEdits);
		Assert.Equal(0, fixture.State.Executions);
		Assert.Equal(0, fixture.State.ScopesCreated);
		Assert.NotEmpty(fixture.Logs);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Exhausted_acknowledgment_deadline_does_not_attempt_transport_or_execute_the_module()
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineSlashCommandInteraction("dispatcher-probe")
		{
			CreatedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5)
		};

		await fixture.HandleAsync(interaction);
		await fixture.DrainAsync();

		Assert.Empty(interaction.Calls);
		Assert.Equal(0, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(0, interaction.ModifyCount);
		Assert.Equal(0, fixture.State.Executions);
		Assert.Equal(0, fixture.State.ScopesCreated);
		Assert.False(interaction.HasResponded);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Unknown_autocomplete_returns_empty_choices_with_the_correct_response_kind()
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineAutocompleteInteraction("missing-autocomplete", "query", version: null!);

		await fixture.HandleAsync(interaction);

		Assert.True(interaction.HasResponded);
		Assert.Equal(1, interaction.AutocompleteResponseAttempts);
		Assert.Equal(1, interaction.AutocompleteRespondCount);
		Assert.Empty(interaction.Choices);
		Assert.Equal(new[] { "autocomplete" }, interaction.Calls);
		Assert.Equal(0, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(0, interaction.ModifyCount);
		Assert.Equal(0, fixture.State.Executions);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Cached_generation_failure_responds_with_empty_choices_once()
	{
		await using var fixture = await Fixture.CreateAsync();
		fixture.Catalog.Publish(new DocumentationSnapshot(null!, ParsedDocs.Empty, ParsedDocs.Empty));
		var interaction = new OfflineAutocompleteInteraction("traits", "query");

		await fixture.HandleAsync(interaction);

		Assert.True(interaction.HasResponded);
		Assert.Equal(1, interaction.AutocompleteResponseAttempts);
		Assert.Equal(1, interaction.AutocompleteRespondCount);
		Assert.Empty(interaction.Choices);
		Assert.Equal(new[] { "autocomplete" }, interaction.Calls);
		Assert.Equal(0, interaction.DeferCount);
		Assert.NotEmpty(fixture.Logs);
		fixture.AssertSafeLogs();
	}

	[Theory]
	[InlineData("traits")]
	[InlineData("dispatcher-autocomplete")]
	public async Task Ambiguous_autocomplete_transport_failure_is_never_retried(string command)
	{
		await using var fixture = await Fixture.CreateAsync();
		var interaction = new OfflineAutocompleteInteraction(command, "query", version: command == "traits" ? "release" : null!)
		{
			AutocompleteResponseHandler = (_, _) => Task.FromException(new InvalidOperationException(
				DispatcherProbeState.PrivatePayload, new Exception(DispatcherProbeState.PrivateInnerPayload)))
		};

		await fixture.HandleAsync(interaction);

		Assert.False(interaction.HasResponded);
		Assert.Equal(1, interaction.AutocompleteResponseAttempts);
		Assert.Equal(0, interaction.AutocompleteRespondCount);
		Assert.Equal(new[] { "autocomplete" }, interaction.Calls);
		Assert.Equal(0, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(0, interaction.ModifyCount);
		Assert.NotEmpty(fixture.Logs);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Expired_autocomplete_is_not_sent_or_retried()
	{
		var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
		await using var fixture = await Fixture.CreateAsync(clock: clock);
		var interaction = new OfflineAutocompleteInteraction("traits", "query")
		{
			CreatedAt = clock.GetUtcNow() - TimeSpan.FromSeconds(5)
		};

		await fixture.HandleAsync(interaction);

		Assert.False(interaction.HasResponded);
		Assert.Equal(0, interaction.AutocompleteResponseAttempts);
		Assert.Equal(0, interaction.AutocompleteRespondCount);
		Assert.Empty(interaction.Calls);
		fixture.AssertSafeLogs();
	}

	[Fact]
	public async Task Autocomplete_transport_uses_a_finite_cancellable_budget_and_stays_awaited()
	{
		var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
		await using var fixture = await Fixture.CreateAsync(clock: clock);
		var receivedOptions = new TaskCompletionSource<RequestOptions?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var releaseTransport = Signal();
		var interaction = new OfflineAutocompleteInteraction("traits", "query")
		{
			CreatedAt = clock.GetUtcNow(),
			AutocompleteResponseHandler = async (_, options) =>
			{
				receivedOptions.TrySetResult(options);
				await releaseTransport.Task.WaitAsync(options?.CancelToken ?? CancellationToken.None);
			}
		};
		var dispatch = fixture.HandleAsync(interaction);
		try
		{
			var options = await receivedOptions.Task.WaitAsync(TimeSpan.FromSeconds(5));
			Assert.NotNull(options);
			Assert.True(options.CancelToken.CanBeCanceled);
			Assert.False(dispatch.IsCompleted);

			clock.Advance(TimeSpan.FromSeconds(2));
			await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

			Assert.True(options.CancelToken.IsCancellationRequested);
			Assert.False(interaction.HasResponded);
			Assert.Equal(1, interaction.AutocompleteResponseAttempts);
			Assert.Equal(0, interaction.AutocompleteRespondCount);
			Assert.Equal(new[] { "autocomplete" }, interaction.Calls);
			fixture.AssertSafeLogs();
		}
		finally
		{
			// Keep a failing test from leaving an uncancellable fake transport behind.
			releaseTransport.TrySetResult();
			await dispatch.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}

	static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

	sealed class Fixture : IAsyncDisposable
	{
		readonly DiscordSocketClient client = new();
		readonly OfflineHostApplicationLifetime lifetime = new();
		readonly ServiceProvider services;
		readonly InteractionDispatcher dispatcher;
		public ConcurrentQueue<RecordedLog> Logs { get; } = new();
		public DispatcherProbeState State { get; } = new();
		public InteractionWorkQueue Queue { get; }
		public DocumentationCatalog Catalog => services.GetRequiredService<DocumentationCatalog>();

		Fixture(int capacity, TimeProvider clock)
		{
			var configuration = new ConfigurationBuilder().AddInMemoryCollection(DocumentationOptionsTests.ValidSettings()).Build();
			services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
				.AddSingleton(State).AddScoped<DispatcherScopedProbe>()
				.AddSingleton(clock).AddDocumentationFeature(configuration)
				.AddSingleton<ILogger<DocumentationAutocompleteResponder>>(new RecordingLogger<DocumentationAutocompleteResponder>(Logs))
				.AddInteractionFramework(client.Rest)
				.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
			Queue = new InteractionWorkQueue(new RecordingLogger<InteractionWorkQueue>(Logs), new WorkerFailureState(), capacity, workerCount: 1);
			dispatcher = new InteractionDispatcher(client, services.GetRequiredService<InteractionService>(), Queue, services, clock,
				new RecordingLogger<InteractionDispatcher>(Logs));
		}

		public static async Task<Fixture> CreateAsync(int capacity = 8, TimeProvider? clock = null)
		{
			var fixture = new Fixture(capacity, clock ?? TimeProvider.System);
			try
			{
				await fixture.services.GetRequiredService<InteractionService>().AddModuleAsync<DispatcherProbeModule>(fixture.services);
				await fixture.services.GetRequiredService<InteractionService>().AddModuleAsync<DocumentationModule>(fixture.services);
				await fixture.Queue.StartAsync(CancellationToken.None);
				return fixture;
			}
			catch
			{
				await fixture.DisposeAsync();
				throw;
			}
		}

		public Task HandleAsync(IDiscordInteraction interaction) =>
			dispatcher.HandleAsync(new OfflineInteractionContext(client, interaction));

		public async Task DrainAsync()
		{
			var drained = Signal();
			Assert.True(Queue.TryEnqueue(_ =>
			{
				drained.TrySetResult();
				return Task.CompletedTask;
			}));
			await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}

		public void AssertSafeLogs() => Assert.All(Logs, entry =>
		{
			Assert.Null(entry.Exception);
			Assert.DoesNotContain(DispatcherProbeState.PrivatePayload, entry.Message);
			Assert.DoesNotContain(DispatcherProbeState.PrivateInnerPayload, entry.Message);
		});

		public async ValueTask DisposeAsync()
		{
			lifetime.StopApplication();
			State.AllowCompletion.TrySetResult();
			try
			{
				await Queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
			}
			finally
			{
				Queue.Dispose();
				await services.DisposeAsync();
				client.Dispose();
				lifetime.Dispose();
			}
		}
	}

	sealed record RecordedLog(string Message, Exception? Exception);

	sealed class RecordingLogger<T>(ConcurrentQueue<RecordedLog> entries) : ILogger<T>
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel level) => true;
		public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter) => entries.Enqueue(new RecordedLog(formatter(state, exception), exception));
	}
}

/// <summary>Coordinates deterministic SDK invocation and scoped-resource observations for dispatcher tests.</summary>
public sealed class DispatcherProbeState
{
	public const string PrivatePayload = "synthetic-private-dispatcher-payload";
	public const string PrivateInnerPayload = "synthetic-private-inner-token";
	public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public TaskCompletionSource AllowCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public TaskCompletionSource ScopeDisposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public DispatcherScopedProbe? ActiveScope { get; set; }
	public int Executions { get; set; }
	public int ScopesCreated { get; set; }
	public int ScopesDisposed { get; set; }
	public bool AcknowledgedAtExecution { get; set; }
	public bool ScopeAliveBeforeResponse { get; set; }
}

/// <summary>Detects a premature or repeated invocation-scope disposal while the SDK command is awaiting completion.</summary>
public sealed class DispatcherScopedProbe : IDisposable
{
	readonly DispatcherProbeState state;
	public bool IsDisposed { get; private set; }

	public DispatcherScopedProbe(DispatcherProbeState state)
	{
		this.state = state;
		state.ScopesCreated++;
		state.ActiveScope = this;
	}

	public void Dispose()
	{
		IsDisposed = true;
		state.ScopesDisposed++;
		state.ScopeDisposed.TrySetResult();
	}
}

/// <summary>Runs real SDK commands with metadata-safe dependencies and explicitly controlled asynchronous completion.</summary>
public sealed class DispatcherProbeModule : InteractionModuleBase<IInteractionContext>
{
	readonly DispatcherProbeState state;
	readonly IServiceProvider services;
	readonly IHostApplicationLifetime lifetime;

	public DispatcherProbeModule(DispatcherProbeState state, IServiceProvider services, IHostApplicationLifetime lifetime)
	{
		this.state = state;
		this.services = services;
		this.lifetime = lifetime;
	}

	[SlashCommand("dispatcher-probe", "Offline invocation-scope probe.")]
	public async Task Probe()
	{
		var resource = services.GetRequiredService<DispatcherScopedProbe>();
		state.Executions++;
		state.AcknowledgedAtExecution = Context.Interaction.HasResponded;
		state.Entered.TrySetResult();
		await state.AllowCompletion.Task.WaitAsync(lifetime.ApplicationStopping);
		state.ScopeAliveBeforeResponse = !resource.IsDisposed;
		await Context.Interaction.ModifyOriginalResponseAsync(properties => properties.Content = "probe-completed",
			new RequestOptions { CancelToken = lifetime.ApplicationStopping });
	}

	[SlashCommand("dispatcher-fault", "Offline module-failure probe.")]
	public Task Fault()
	{
		state.Executions++;
		throw new InvalidOperationException(DispatcherProbeState.PrivatePayload,
			new Exception(DispatcherProbeState.PrivateInnerPayload));
	}

	[SlashCommand("dispatcher-autocomplete", "Offline autocomplete-failure probe.")]
	public Task Autocomplete([Autocomplete] string? name = null) => Task.CompletedTask;

	[AutocompleteCommand(parameterName: "name", commandName: "dispatcher-autocomplete")]
	public Task AutocompleteSuggestions() => ((IAutocompleteInteraction)Context.Interaction).RespondAsync(
		Array.Empty<AutocompleteResult>(), new RequestOptions { CancelToken = lifetime.ApplicationStopping });
}
