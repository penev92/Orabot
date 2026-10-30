using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	/// <summary>Loads attributed interaction metadata once for both runtime dispatch and offline deployment tools.</summary>
	public sealed class ApplicationCommandCatalog
	{
		private readonly InteractionService _interactions;
		private readonly object _sync = new();
		private Task _initialization;
		private IReadOnlyList<ApplicationCommandProperties> _definitions;

		/// <summary>Uses the same interaction service that will dispatch runtime commands.</summary>
		public ApplicationCommandCatalog(InteractionService interactions) => _interactions = interactions;

		/// <summary>Discovers this assembly's modules exactly once without logging in or executing commands.</summary>
		public async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Task initialization;
			lock (_sync)
				initialization = _initialization ??= InitializeCoreAsync(services);
			// SDK metadata registration has no cancellation parameter. Complete its owned
			// operation before reporting caller cancellation rather than abandoning it.
			await initialization;
			cancellationToken.ThrowIfCancellationRequested();
		}

		/// <summary>Returns independent definitions sorted by type and ordinal name after initialization.</summary>
		public IReadOnlyList<ApplicationCommandProperties> GetDefinitions()
		{
			if (_definitions == null)
				throw new InvalidOperationException("Initialize the application-command catalog first.");
			return _definitions.Select(x => ApplicationCommandSchema.Prepare(x, ApplicationCommandScope.Global)).ToArray();
		}

		private async Task InitializeCoreAsync(IServiceProvider services)
		{
			try
			{
				await _interactions.AddModulesAsync(typeof(ApplicationCommandCatalog).Assembly, services);
			}
			catch (Exception exception)
			{
				throw new InvalidOperationException($"Application-command metadata initialization failed with {exception.GetType().FullName}.");
			}
			if (_interactions.SlashCommands.Any(x => x.RunMode == RunMode.Async)
				|| _interactions.ContextCommands.Any(x => x.RunMode == RunMode.Async)
				|| _interactions.ComponentCommands.Any(x => x.RunMode == RunMode.Async)
				|| _interactions.ModalCommands.Any(x => x.RunMode == RunMode.Async)
				|| _interactions.Modules.SelectMany(x => x.AutocompleteCommands).Any(x => x.RunMode == RunMode.Async))
				throw new NotSupportedException("Interaction metadata must use synchronous run mode inside the owned work queue.");

			var definitions = CommandDefinitionProjection.Project(_interactions)
				.OrderBy(ApplicationCommandSchema.TypeOf).ThenBy(x => x.Name.Value, StringComparer.Ordinal).ToArray();
			ApplicationCommandSchema.EnsureUnique(definitions);
			_definitions = definitions;
		}
	}
}
