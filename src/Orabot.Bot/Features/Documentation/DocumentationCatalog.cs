using Orabot.Bot.Features.Documentation.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>One complete generation of release, playtest and development search data.</summary>
	public sealed record DocumentationSnapshot(ParsedDocs ReleaseDocs, ParsedDocs PlaytestDocs, ParsedDocs DevelopmentDocs);

	/// <summary>A cached suggestion whose label and submitted value satisfy Discord's string limits.</summary>
	public sealed record DocumentationSuggestion(string Name, string Value);

	/// <summary>Provides fast documentation reads independently of the hosted refresh worker.</summary>
	public sealed class DocumentationCatalog
	{
		private DocumentationSnapshot _snapshot = new(ParsedDocs.Empty, ParsedDocs.Empty, ParsedDocs.Empty);

		/// <summary>Captures a complete generation so a reader never observes a partially refreshed collection.</summary>
		public DocumentationSnapshot Snapshot => Volatile.Read(ref _snapshot);

		internal void Publish(DocumentationSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);

		/// <summary>Returns cached autocomplete choices without network access, including during cold startup.</summary>
		public IReadOnlyList<DocumentationSuggestion> Suggest(DocumentationKind kind, string version, string query)
		{
			if (!DocumentationLookupService.IsVersionSupported(version) || (query?.Length ?? 0) > 100)
				return Array.Empty<DocumentationSuggestion>();
			var snapshot = Snapshot;
			var docs = version switch
			{
				"playtest" => snapshot.PlaytestDocs,
				"development" => snapshot.DevelopmentDocs,
				_ => snapshot.ReleaseDocs
			};
			var entries = kind switch
			{
				DocumentationKind.Traits => docs.Traits,
				DocumentationKind.Weapons => docs.Weapons,
				DocumentationKind.Lua => docs.Lua,
				DocumentationKind.SpriteSequences => docs.SpriteSequences,
				_ => throw new ArgumentOutOfRangeException(nameof(kind))
			};
			return entries.Where(entry => entry.Key.Length is > 0 and <= 100
					&& entry.Value.Title.Length is > 0 and <= 100
					&& entry.Key.Contains(query ?? string.Empty, StringComparison.InvariantCultureIgnoreCase))
				.Take(25).Select(entry => new DocumentationSuggestion(entry.Key, entry.Value.Title)).ToArray();
		}
	}
}
