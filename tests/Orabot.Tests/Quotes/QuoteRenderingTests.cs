using Orabot.Bot.Features.Quotes;

namespace Orabot.Tests.Quotes;

/// <summary>Preserves source attribution, links and images while keeping Discord output within its limits.</summary>
public sealed class QuoteRenderingTests
{
	[Fact]
	public void Source_embed_preserves_author_channel_time_original_link_and_image()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var message = source.AddMessage(100, "Text", imageUrl: "https://example.org/image.png");
		var embed = QuoteEmbedRenderer.CreateSourceEmbed(1, source.Channel, new[] { message });
		Assert.Equal("Author30 nick", embed.Author!.Value.Name);
		Assert.Equal("https://example.org/avatar.png", embed.Author.Value.IconUrl);
		Assert.Equal("2020-01-02 03:04:05 UTC, in #source-200", embed.Footer!.Value.Text);
		Assert.Contains("[Original message](https://discord.com/channels/1/200/100)", embed.Description);
		Assert.Equal("https://example.org/image.png", embed.Image!.Value.Url);
	}

	[Fact]
	public void Long_source_content_is_truncated_without_losing_original_link()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var message = source.AddMessage(100, new string('x', 20000));
		var embed = QuoteEmbedRenderer.CreateSourceEmbed(1, source.Channel, new[] { message });
		Assert.Equal(4096, embed.Description.Length);
		Assert.EndsWith("[Original message](https://discord.com/channels/1/200/100)", embed.Description);
	}

	[Theory]
	[InlineData("https://example.org/image.png", "https://example.org/image.png")]
	[InlineData("file:///private/path", null)]
	public void Only_http_links_are_used_as_content_images(string content, string? expected)
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var message = source.AddMessage(100, content);
		var embed = QuoteEmbedRenderer.CreateSourceEmbed(1, source.Channel, new[] { message });
		Assert.Equal(expected, embed.Image?.Url);
	}
}
