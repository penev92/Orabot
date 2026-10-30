using System.Text.Json.Serialization;

namespace Orabot.Bot.Features.Documentation.Models
{
	/// <summary>Represents one entry in the OpenRA documentation search-index response.</summary>
	public class DocsEntry
	{
		[JsonPropertyName("location")]
		public string Location { get; set; }

		[JsonPropertyName("text")]
		public string Text { get; set; }

		[JsonPropertyName("title")]
		public string Title { get; set; }
	}
}
