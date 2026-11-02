using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace Orabot.Bot.Discord.Interactions
{
	/// <summary>Owns initial acknowledgments, queued framework execution and safe interaction failures.</summary>
	internal sealed class InteractionDispatcher : IInteractionDispatcher
	{
		private readonly DiscordSocketClient _client;
		private readonly InteractionService _interactions;
		private readonly InteractionWorkQueue _queue;
		private readonly IServiceProvider _moduleServices;
		private readonly TimeProvider _clock;
		private readonly ILogger<InteractionDispatcher> _logger;

		public InteractionDispatcher(DiscordSocketClient client, InteractionService interactions,
			InteractionWorkQueue queue, IServiceProvider moduleServices,
			TimeProvider clock, ILogger<InteractionDispatcher> logger)
		{
			_client = client;
			_interactions = interactions;
			_queue = queue;
			_moduleServices = moduleServices;
			_clock = clock;
			_logger = logger;
		}

		public async Task InitializeAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await _interactions.AddModulesAsync(typeof(InteractionDispatcher).Assembly, _moduleServices);
			cancellationToken.ThrowIfCancellationRequested();
		}

		public Task HandleInteractionAsync(SocketInteraction interaction) =>
			HandleAsync(new SocketInteractionContext(_client, interaction));

		internal async Task HandleAsync(IInteractionContext context)
		{
			var interaction = context.Interaction;
			if (interaction.HasResponded)
				return;
			if (interaction.Type == InteractionType.ApplicationCommandAutocomplete)
			{
				await HandleAutocompleteAsync(context);
				return;
			}
			if (interaction.Type != InteractionType.ApplicationCommand)
			{
				await ReplyBeforeAcknowledgmentAsync(interaction, "This interaction is not available in this version of Orabot.");
				return;
			}

			var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!_queue.TryEnqueue(stoppingToken => ExecuteQueuedAsync(context, acknowledged.Task, stoppingToken)))
			{
				await ReplyBeforeAcknowledgmentAsync(interaction, "Orabot is busy. Please try again shortly.");
				return;
			}
			try
			{
				using var deadline = InteractionResponseDeadline.Create(interaction, _clock);
				// Modules edit this public deferred response; they never own a second initial response.
				await interaction.DeferAsync(ephemeral: false, options: new RequestOptions { CancelToken = deadline.Token, RetryMode = RetryMode.RetryRatelimit });
				acknowledged.TrySetResult();
			}
			catch (Exception exception)
			{
				acknowledged.TrySetException(exception);
				_logger.LogWarning("Interaction {InteractionId} could not be acknowledged ({ExceptionType}).",
					interaction.Id, exception.GetType().FullName);
				// A transport timeout does not prove the acknowledgment was not accepted. Do not retry it.
			}
		}

		private async Task ExecuteQueuedAsync(IInteractionContext context, Task acknowledged, CancellationToken stoppingToken)
		{
			try
			{
				// Observe acknowledgment failures even if shutdown has already been requested.
				await acknowledged;
				stoppingToken.ThrowIfCancellationRequested();
				var result = await _interactions.ExecuteCommandAsync(context, _moduleServices);
				if (!result.IsSuccess)
				{
					_logger.LogWarning("Interaction {InteractionId} failed with {InteractionError}.", context.Interaction.Id, result.Error);
					await CompleteFailureAsync(context.Interaction, FailureMessage(result.Error));
				}
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				if (acknowledged.IsCompletedSuccessfully)
					await CompleteFailureAsync(context.Interaction, "Orabot is shutting down. Please try again shortly.");
			}
			catch (Exception exception)
			{
				_logger.LogWarning("Interaction {InteractionId} failed ({ExceptionType}).",
					context.Interaction.Id, exception.GetType().FullName);
				if (acknowledged.IsCompletedSuccessfully)
					await CompleteFailureAsync(context.Interaction, FailureMessage(InteractionCommandError.Exception));
			}
		}

		private async Task HandleAutocompleteAsync(IInteractionContext context)
		{
			var safeToRespond = false;
			try
			{
				// Explicit SDK autocomplete commands own their single bounded response. A
				// handler failure may follow an accepted send, so only routing/precondition
				// errors that precede invocation may receive an empty response here.
				var result = await _interactions.ExecuteCommandAsync(context, _moduleServices);
				if (!result.IsSuccess)
				{
					_logger.LogWarning("Autocomplete {InteractionId} failed with {InteractionError}.", context.Interaction.Id, result.Error);
					safeToRespond = result.Error is InteractionCommandError.UnknownCommand or InteractionCommandError.UnmetPrecondition
						or InteractionCommandError.BadArgs or InteractionCommandError.ConvertFailed or InteractionCommandError.ParseFailed;
				}
			}
			catch (Exception exception)
			{
				_logger.LogWarning("Autocomplete {InteractionId} failed ({ExceptionType}).", context.Interaction.Id, exception.GetType().FullName);
			}
			if (safeToRespond && !context.Interaction.HasResponded && context.Interaction is IAutocompleteInteraction autocomplete)
			{
				try
				{
					using var deadline = InteractionResponseDeadline.Create(context.Interaction, _clock);
					await autocomplete.RespondAsync(Array.Empty<AutocompleteResult>(), new RequestOptions { CancelToken = deadline.Token, RetryMode = RetryMode.RetryRatelimit });
				}
				catch (Exception exception)
				{
					_logger.LogWarning("Autocomplete {InteractionId} response failed ({ExceptionType}).", context.Interaction.Id, exception.GetType().FullName);
				}
			}
		}

		private async Task ReplyBeforeAcknowledgmentAsync(IDiscordInteraction interaction, string message)
		{
			try
			{
				using var deadline = InteractionResponseDeadline.Create(interaction, _clock);
				await interaction.RespondAsync(message, ephemeral: true, allowedMentions: AllowedMentions.None,
					options: new RequestOptions { CancelToken = deadline.Token, RetryMode = RetryMode.RetryRatelimit });
			}
			catch (Exception exception)
			{
				_logger.LogWarning("Interaction {InteractionId} response failed ({ExceptionType}).", interaction.Id, exception.GetType().FullName);
			}
		}

		private async Task CompleteFailureAsync(IDiscordInteraction interaction, string message)
		{
			try
			{
				using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
				await interaction.ModifyOriginalResponseAsync(properties =>
				{
					properties.Content = message;
					properties.Embeds = Array.Empty<Embed>();
					properties.AllowedMentions = AllowedMentions.None;
				}, new RequestOptions { CancelToken = deadline.Token });
			}
			catch (Exception exception)
			{
				_logger.LogWarning("Interaction {InteractionId} completion failed ({ExceptionType}).", interaction.Id, exception.GetType().FullName);
			}
		}

		private static string FailureMessage(InteractionCommandError? error) => error switch
		{
			InteractionCommandError.UnknownCommand => "This command is not available in this version of Orabot.",
			InteractionCommandError.BadArgs or InteractionCommandError.ConvertFailed or InteractionCommandError.ParseFailed =>
				"Check the command options and try again.",
			InteractionCommandError.UnmetPrecondition => "You cannot use this command here.",
			_ => "The command could not be completed. Please try again later."
		};
	}
}
