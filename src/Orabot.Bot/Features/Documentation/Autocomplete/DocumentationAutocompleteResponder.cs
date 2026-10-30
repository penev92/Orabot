using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;
using Orabot.Bot.Discord.Interactions;

namespace Orabot.Bot.Features.Documentation.Autocomplete
{
	/// <summary>Owns cached suggestion generation and one bounded initial autocomplete response.</summary>
	public sealed class DocumentationAutocompleteResponder
	{
		private readonly DocumentationCatalog _catalog;
		private readonly TimeProvider _clock;
		private readonly ILogger<DocumentationAutocompleteResponder> _logger;

		/// <summary>Uses only the in-memory catalog and inert transport-policy dependencies.</summary>
		public DocumentationAutocompleteResponder(DocumentationCatalog catalog, TimeProvider clock,
			ILogger<DocumentationAutocompleteResponder> logger)
		{
			_catalog = catalog;
			_clock = clock;
			_logger = logger;
		}

		/// <summary>Returns cached suggestions or an empty generation fallback, without retrying an attempted response.</summary>
		public async Task RespondAsync(IAutocompleteInteraction interaction, DocumentationKind kind, CancellationToken stoppingToken = default)
		{
			AutocompleteResult[] suggestions;
			try
			{
				var version = interaction.Data.Options.FirstOrDefault(option => option.Name == "version")?.Value as string ?? "release";
				var query = interaction.Data.Current.Value as string ?? string.Empty;
				suggestions = _catalog.Suggest(kind, version, query)
					.Select(item => new AutocompleteResult(item.Name, item.Value)).ToArray();
			}
			catch (Exception exception)
			{
				_logger.LogWarning("Documentation autocomplete {InteractionId} generation failed ({ExceptionType}).",
					interaction.Id, exception.GetType().FullName);
				suggestions = Array.Empty<AutocompleteResult>();
			}

			try
			{
				using var deadline = InteractionResponseDeadline.Create(interaction, _clock);
				using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, stoppingToken);
				await interaction.RespondAsync(suggestions, new RequestOptions
				{
					CancelToken = cancellation.Token,
					RetryMode = RetryMode.RetryRatelimit
				});
			}
			catch (Exception exception)
			{
				// A failed transport await does not prove Discord rejected the response.
				// The module owns this attempt; neither this helper nor the dispatcher retries it.
				_logger.LogWarning("Documentation autocomplete {InteractionId} response failed ({ExceptionType}).",
					interaction.Id, exception.GetType().FullName);
			}
		}
	}
}
