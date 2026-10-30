using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Orabot.Bot.Features.Documentation.Models
{
	public class ParsedDocs
	{
		private static readonly IReadOnlyDictionary<string, DocsEntry> EmptyEntries =
			new ReadOnlyDictionary<string, DocsEntry>(new Dictionary<string, DocsEntry>());

		public static ParsedDocs Empty { get; } = new ParsedDocs();

		public IReadOnlyDictionary<string, DocsEntry> Traits { get; init; } = EmptyEntries;

		public IReadOnlyDictionary<string, DocsEntry> Weapons { get; init; } = EmptyEntries;

		public IReadOnlyDictionary<string, DocsEntry> SpriteSequences { get; init; } = EmptyEntries;

		public IReadOnlyDictionary<string, DocsEntry> Lua { get; init; } = EmptyEntries;
	}
}
