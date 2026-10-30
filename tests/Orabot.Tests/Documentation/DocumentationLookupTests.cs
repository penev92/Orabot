using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Rendering;

namespace Orabot.Tests.Documentation;

/// <summary>Tests server-side validation, page fallbacks and I/O-free presentation.</summary>
public sealed class DocumentationLookupTests
{
	[Theory]
	[InlineData("", "release")]
	[InlineData(null, "development")]
	public async Task Empty_name_uses_configured_page_without_fetching_html(string? name, string version)
	{
		var pages = new FakePages();
		var lookup = new DocumentationLookupService(DocumentationOptionsTests.Links(), pages);
		var result = await lookup.LookupAsync(DocumentationKind.Traits, name!, version, CancellationToken.None);
		Assert.Equal("https://example.org/traits/" + version, result.TargetUrl);
		Assert.Null(result.Name);
		Assert.Equal(0, pages.Calls);
	}

	[Theory]
	[InlineData("Valid", "unsupported")]
	[InlineData("line\nbreak", "release")]
	public async Task Invalid_submission_is_rejected_before_http(string name, string version)
	{
		var pages = new FakePages();
		var lookup = new DocumentationLookupService(DocumentationOptionsTests.Links(), pages);
		await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookupAsync(DocumentationKind.Traits, name, version, CancellationToken.None));
		await Assert.ThrowsAsync<ArgumentException>(() => lookup.LookupAsync(DocumentationKind.Traits, new string('x', 101), "release", CancellationToken.None));
		Assert.Equal(0, pages.Calls);
	}

	[Fact]
	public async Task Unknown_name_preserves_family_page_fallback()
	{
		var lookup = new DocumentationLookupService(DocumentationOptionsTests.Links(), new FakePages());
		var result = await lookup.LookupAsync(DocumentationKind.Traits, "MissingTrait", "release", CancellationToken.None);
		var embed = new DocumentationEmbedRenderer(DocumentationOptionsTests.Links()).CreateEmbed(result);
		Assert.Equal("https://example.org/traits/release", embed.Url);
		Assert.Equal("OpenRA Traits page", embed.Author!.Value.Name);
		Assert.StartsWith("This documentation is aimed at modders.", embed.Description);
	}

	[Theory]
	[InlineData(DocumentationKind.Traits, "OpenRA Traits page, trait Known")]
	[InlineData(DocumentationKind.Weapons, "OpenRA Weapons page, weapon Known")]
	[InlineData(DocumentationKind.Lua, "OpenRA Lua API page, table Known")]
	[InlineData(DocumentationKind.SpriteSequences, "OpenRA Sprite Sequences page, Known")]
	public async Task Matched_entry_keeps_existing_embed_labels_and_family_description(DocumentationKind kind, string expectedAuthor)
	{
		var pages = new FakePages { Entry = new DocumentationPageEntry(true, "First description paragraph") };
		var result = await new DocumentationLookupService(DocumentationOptionsTests.Links(), pages)
			.LookupAsync(kind, "Known", "playtest", CancellationToken.None);
		var embed = new DocumentationEmbedRenderer(DocumentationOptionsTests.Links()).CreateEmbed(result);
		Assert.Equal(expectedAuthor, embed.Author!.Value.Name);
		Assert.EndsWith("#known", embed.Url);
		Assert.Equal(kind == DocumentationKind.Lua ? "This documentation is aimed at scripted map creators." : "First description paragraph", embed.Description);
	}

	[Fact]
	public void Long_html_description_is_bounded_before_discord_embed_construction()
	{
		var result = new DocumentationLookupResult(DocumentationKind.Traits, "Known", "https://example.org/#known", new string('x', 10000));
		var embed = new DocumentationEmbedRenderer(DocumentationOptionsTests.Links()).CreateEmbed(result);
		Assert.Equal(4096, embed.Description.Length);
	}

	private sealed class FakePages : IDocumentationPageClient
	{
		internal int Calls { get; private set; }
		internal DocumentationPageEntry Entry { get; set; } = new(false, null!);
		public Task<DocumentationPageEntry> FindEntryAsync(Uri page, string name, CancellationToken cancellationToken)
		{
			Calls++;
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(Entry);
		}
	}
}
