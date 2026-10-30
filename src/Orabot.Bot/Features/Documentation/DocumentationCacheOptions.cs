namespace Orabot.Bot.Features.Documentation
{
	public sealed class DocumentationCacheOptions
	{
		public const string SectionName = "DocsSearchIndex";
		public int ScanInterval { get; set; } = 300000;
	}
}
