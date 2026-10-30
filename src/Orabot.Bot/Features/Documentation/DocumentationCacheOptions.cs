namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Controls the interval between complete documentation-index refreshes.</summary>
	public sealed class DocumentationCacheOptions
	{
		public const string SectionName = "DocsSearchIndex";
		public int ScanInterval { get; set; } = 300000;
	}
}
