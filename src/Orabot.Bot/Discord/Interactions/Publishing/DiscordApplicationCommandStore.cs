using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	/// <summary>Adapts an already authenticated Discord REST client to individual command operations.</summary>
	public sealed class DiscordApplicationCommandStore : IApplicationCommandStore
	{
		private readonly DiscordRestClient _client;
		private readonly Dictionary<ApplicationCommandScope, IReadOnlyDictionary<ulong, RestApplicationCommand>> _lastRead = new();

		/// <summary>Uses caller-owned authentication and disposal; does not log in, publish or subscribe to SDK logs.</summary>
		public DiscordApplicationCommandStore(DiscordRestClient client) => _client = client;

		/// <inheritdoc />
		public async Task<IReadOnlyList<RemoteApplicationCommand>> ListAsync(ApplicationCommandScope scope, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var options = new RequestOptions { CancelToken = cancellationToken };
			RestApplicationCommand[] commands = scope.IsGlobal
				? (await _client.GetGlobalApplicationCommands(true, options: options)).Cast<RestApplicationCommand>().ToArray()
				: (await _client.GetGuildApplicationCommands(scope.GuildId.Value, true, options: options)).Cast<RestApplicationCommand>().ToArray();
			_lastRead[scope] = commands.ToDictionary(x => x.Id);
			return commands.Select(x => new RemoteApplicationCommand(x.Id, ApplicationCommandSchema.FromRemote(x))).ToArray();
		}

		/// <inheritdoc />
		public async Task UpsertAsync(ApplicationCommandScope scope, ApplicationCommandProperties definition, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var properties = ApplicationCommandSchema.Prepare(definition, scope);
			var options = new RequestOptions { CancelToken = cancellationToken, RetryMode = RetryMode.RetryRatelimit };
			// Discord's create endpoints upsert an existing type/name while retaining its ID.
			// Individual requests avoid bulk-overwrite's implicit removal of other commands.
			if (scope.IsGlobal)
				await _client.CreateGlobalCommand(properties, options);
			else
				await _client.CreateGuildCommand(properties, scope.GuildId.Value, options);
		}

		/// <inheritdoc />
		public async Task DeleteAsync(ApplicationCommandScope scope, ulong commandId, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!_lastRead.TryGetValue(scope, out var commands) || !commands.TryGetValue(commandId, out var command))
				throw new InvalidOperationException("Read the deployment scope before deleting a command identity.");
			await command.DeleteAsync(new RequestOptions { CancelToken = cancellationToken, RetryMode = RetryMode.RetryRatelimit });
		}
	}
}
