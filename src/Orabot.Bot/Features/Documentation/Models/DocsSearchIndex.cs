using System.Text.Json.Serialization;

namespace Orabot.Bot.Features.Documentation.Models
{
	/// <summary>Describes the external search-index payload consumed by the documentation refresh worker.</summary>
	public class DocsSearchIndex
	{
		[JsonPropertyName("config")]
		public object Config { get; set; }

		[JsonPropertyName("docs")]
		public DocsEntry[] Docs { get; set; }
	}
}
