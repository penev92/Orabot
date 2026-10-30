using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orabot.Bot.Discord.Commands;
using Orabot.Bot.Discord.Interactions;
using Orabot.Bot.Discord.Logging;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Discord
{
	/// <summary>Owns the Discord connection lifetime, but not the container-owned clients.</summary>
	public sealed class Bot : IHostedService
	{
		private readonly DiscordSocketClient _client;
		private readonly CommandService _commands;
		private readonly IServiceProvider _moduleServices;
		private readonly IReadOnlyList<BaseTypeReader> _typeReaders;
		private readonly IReadOnlyList<ModuleBase<SocketCommandContext>> _modules;
		private readonly ILogEventHandler _logEventHandler;
		private readonly IMessageEventHandler _messageEventHandler;
		private readonly IReactionEventHandler _reactionEventHandler;
		private readonly ISlashCommandEventHandler _slashCommandHandler;
		private readonly IAutocompleteEventHandler _autocompleteHandler;
		private readonly DiscordReadiness _readiness;
		private readonly DiscordBotOptions _options;
		private readonly ILogger<Bot> _logger;
		private readonly IDiscordConnectionLifecycle _connection;
		private readonly object _stateSync = new();
		private bool _startAttempted;
		private bool _shutdownRequired;
		private bool _eventsAttached;
		private bool _hasBeenReady;
		private bool _stopping;

		public Bot(
			DiscordSocketClient client,
			CommandService commands,
			IServiceProvider moduleServices,
			IEnumerable<BaseTypeReader> typeReaders,
			IEnumerable<ModuleBase<SocketCommandContext>> modules,
			ILogEventHandler logEventHandler,
			IMessageEventHandler messageEventHandler,
			IReactionEventHandler reactionEventHandler,
			ISlashCommandEventHandler slashCommandHandler,
			IAutocompleteEventHandler autocompleteHandler,
			DiscordReadiness readiness,
			IOptions<DiscordBotOptions> options,
			ILogger<Bot> logger)
			: this(client, commands, moduleServices, typeReaders, modules, logEventHandler,
				messageEventHandler, reactionEventHandler, slashCommandHandler, autocompleteHandler,
				readiness, options, logger, new DiscordConnectionLifecycle(client))
		{
		}

		internal Bot(
			DiscordSocketClient client,
			CommandService commands,
			IServiceProvider moduleServices,
			IEnumerable<BaseTypeReader> typeReaders,
			IEnumerable<ModuleBase<SocketCommandContext>> modules,
			ILogEventHandler logEventHandler,
			IMessageEventHandler messageEventHandler,
			IReactionEventHandler reactionEventHandler,
			ISlashCommandEventHandler slashCommandHandler,
			IAutocompleteEventHandler autocompleteHandler,
			DiscordReadiness readiness,
			IOptions<DiscordBotOptions> options,
			ILogger<Bot> logger,
			IDiscordConnectionLifecycle connection)
		{
			_client = client;
			_commands = commands;
			_moduleServices = moduleServices;
			_typeReaders = typeReaders.ToArray();
			_modules = modules.ToArray();
			_logEventHandler = logEventHandler;
			_messageEventHandler = messageEventHandler;
			_reactionEventHandler = reactionEventHandler;
			_slashCommandHandler = slashCommandHandler;
			_autocompleteHandler = autocompleteHandler;
			_readiness = readiness;
			_options = options.Value;
			_logger = logger;
			_connection = connection;
		}

		public async Task StartAsync(CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (_startAttempted)
				throw new InvalidOperationException("The Discord hosted service can only be started once.");

			_startAttempted = true;
			AttachEventHandlers();
			try
			{
				foreach (var typeReader in _typeReaders)
					_commands.AddTypeReader(typeReader.SupportedType, typeReader);

				foreach (var module in _modules)
				{
					cancellationToken.ThrowIfCancellationRequested();
					// Discord.Net needs a provider to activate command modules; other dependencies are explicit.
					await _commands.AddModuleAsync(module.GetType(), _moduleServices);
				}

				cancellationToken.ThrowIfCancellationRequested();
				_shutdownRequired = true;
				_logger.LogInformation("Starting the Discord connection.");
				await _connection.LoginAsync(_options.BotToken);
				cancellationToken.ThrowIfCancellationRequested();
				await _connection.StartAsync();
				cancellationToken.ThrowIfCancellationRequested();
			}
			catch (Exception startupException)
			{
				MarkStopping();
				try
				{
					await ShutdownConnectionAsync();
				}
				catch (Exception exception)
				{
					_logger.LogWarning("Discord cleanup after failed startup failed with {ExceptionType}.", exception.GetType().FullName);
				}
				finally
				{
					DetachEventHandlers();
				}
				if (startupException is OperationCanceledException && cancellationToken.IsCancellationRequested)
					throw;

				// Generic Host logs escaped exceptions before the console entry point can
				// handle them. Do not attach the original SDK exception or its payload.
				throw new InvalidOperationException($"Discord startup failed with {startupException.GetType().FullName}.");
			}
		}

		public async Task StopAsync(CancellationToken cancellationToken)
		{
			MarkStopping();
			try
			{
				// Discord.Net's stop/logout APIs do not accept cancellation. Await them so the
				// container cannot dispose a client while an abandoned shutdown still uses it.
				await ShutdownConnectionAsync();
			}
			catch (Exception shutdownException)
			{
				if (shutdownException is OperationCanceledException && cancellationToken.IsCancellationRequested)
					throw;

				throw new InvalidOperationException($"Discord shutdown failed with {shutdownException.GetType().FullName}.");
			}
			finally
			{
				DetachEventHandlers();
			}
		}

		private async Task ShutdownConnectionAsync()
		{
			if (!_shutdownRequired)
				return;

			_shutdownRequired = false;
			_logger.LogInformation("Stopping the Discord connection.");
			try
			{
				await _connection.StopAsync();
			}
			finally
			{
				await _connection.LogoutAsync();
			}
		}

		private void AttachEventHandlers()
		{
			_client.Log += _logEventHandler.Log;
			_commands.Log += _logEventHandler.Log;
			_client.ReactionAdded += _reactionEventHandler.HandleReactionAddedAsync;
			_client.ReactionRemoved += _reactionEventHandler.HandleReactionRemovedAsync;
			_client.MessageReceived += _messageEventHandler.HandleMessageReceivedAsync;
			_client.SlashCommandExecuted += _slashCommandHandler.HandleSlashCommandAsync;
			_client.AutocompleteExecuted += _autocompleteHandler.HandleAutocompleteAsync;
			_client.Connected += OnConnectedAsync;
			_client.Disconnected += OnDisconnectedAsync;
			_client.Ready += OnReadyAsync;
			_eventsAttached = true;
		}

		private void DetachEventHandlers()
		{
			if (!_eventsAttached)
				return;

			_client.Log -= _logEventHandler.Log;
			_commands.Log -= _logEventHandler.Log;
			_client.ReactionAdded -= _reactionEventHandler.HandleReactionAddedAsync;
			_client.ReactionRemoved -= _reactionEventHandler.HandleReactionRemovedAsync;
			_client.MessageReceived -= _messageEventHandler.HandleMessageReceivedAsync;
			_client.SlashCommandExecuted -= _slashCommandHandler.HandleSlashCommandAsync;
			_client.AutocompleteExecuted -= _autocompleteHandler.HandleAutocompleteAsync;
			_client.Connected -= OnConnectedAsync;
			_client.Disconnected -= OnDisconnectedAsync;
			_client.Ready -= OnReadyAsync;
			_eventsAttached = false;
		}

		internal Task OnConnectedAsync()
		{
			// A resumed session raises Connected but not Ready. Once initial cache readiness
			// is established, reopen the gate after either successful gateway handshake.
			lock (_stateSync)
			{
				if (_hasBeenReady && !_stopping)
					_readiness.MarkReady();
			}
			_logger.LogInformation("Discord gateway connected.");
			return Task.CompletedTask;
		}

		internal Task OnReadyAsync()
		{
			lock (_stateSync)
			{
				if (!_stopping)
				{
					_hasBeenReady = true;
					_readiness.MarkReady();
					_logger.LogInformation("Discord initial guild data is ready.");
				}
			}
			// Worker startup and application-command publication are not gateway-event responsibilities.
			return Task.CompletedTask;
		}

		internal Task OnDisconnectedAsync(Exception exception)
		{
			lock (_stateSync)
				_readiness.MarkDisconnected();
			_logger.LogInformation("Discord gateway disconnected; exception type: {ExceptionType}.", exception?.GetType().FullName ?? "none");
			return Task.CompletedTask;
		}

		private void MarkStopping()
		{
			lock (_stateSync)
			{
				_stopping = true;
				_readiness.MarkDisconnected();
			}
		}
	}
}
