using System.Net;
using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.GitHubReferences;
using Orabot.Bot.Features.GitHubReferences.Messages;
using Orabot.Bot.Features.Maps.Models;
using Orabot.Bot.Features.Maps.Rendering;
using Orabot.Tests.TestDoubles;
using RestSharp;

namespace Orabot.Tests.Integrations;

/// <summary>Exercises passive rendering through a fake HTTP transport and public Discord interfaces.</summary>
public sealed class MapAndGitHubTests
{
	[Fact]
	public async Task Map_author_is_one_escaped_path_segment_and_optional_upstream_text_is_safe()
	{
		using var http = new HttpClient(new ReplyHandler(_ => new StringContent(".mod_ra {color: #123456;}")));
		using var rest = new RestClient(http);
		var renderer = new OpenRaResourceCenterMapLinkToEmbedTransformer(rest);
		var embed = await renderer.CreateEmbed(Map());
		Assert.NotNull(embed);
		Assert.Equal("https://resource.openra.net/maps/author/A%2FB%20%23C/", embed.Author!.Value.Url);
		Assert.Equal((uint)0x123456, embed.Color!.Value.RawValue);
		Assert.True(embed.Title.Length <= 256);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("bad")]
	[InlineData("0,0,-1,10")]
	public async Task Malformed_map_bounds_produce_no_embed_or_request(string? bounds)
	{
		using var http = new HttpClient(new ReplyHandler(_ => throw new InvalidOperationException("No request expected.")));
		using var rest = new RestClient(http);
		var map = Map(); map.Bounds = bounds!;
		Assert.Null(await new OpenRaResourceCenterMapLinkToEmbedTransformer(rest).CreateEmbed(map));
	}

	[Fact]
	public async Task GitHub_null_body_and_unknown_status_render_without_mentions_and_duplicate_requests()
	{
		var calls = 0;
		using var http = new HttpClient(new ReplyHandler(_ =>
		{
			calls++;
			return new StringContent("{\"html_url\":\"https://github.com/OpenRA/OpenRA/issues/3\",\"state\":\"unknown\",\"title\":\"Test\",\"body\":null,\"created_at\":\"2026-01-01T00:00:00Z\"}", System.Text.Encoding.UTF8, "application/json");
		}));
		using var rest = new RestClient(http);
		AllowedMentions? mentions = null;
		var channel = DiscordTestProxy.Create<IMessageChannel>((method, args) => method switch
		{
			"SendMessageAsync" => Record(args!), _ => throw new NotSupportedException(method)
		});
		Task<IUserMessage> Record(object?[] args)
		{
			mentions = args.OfType<AllowedMentions>().Single();
			return Task.FromResult<IUserMessage>(null!);
		}
		var message = DiscordTestProxy.Create<IUserMessage>((method, _) => method switch
		{
			"get_Content" => "ora#3 ORA#3 ora#3", "get_Channel" => channel, _ => throw new NotSupportedException(method)
		});
		var handler = new OpenRaGitHubIssueNumberMessageHandler(rest, Options.Create(new GitHubReferenceOptions { GitHubIconsBaseUrl = "https://example.org/icons" }));
		Assert.True(handler.CanHandle(message));
		await handler.InvokeAsync(message);
		Assert.Equal(1, calls);
		Assert.Same(AllowedMentions.None, mentions);
	}

	static MapInfo Map() => new() { Id = 7, Title = new string('x', 300), Author = "A/B #C", GameMod = "ra", Bounds = "0,0,10,20", Players = 2 };
	sealed class ReplyHandler(Func<HttpRequestMessage, HttpContent> reply) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = reply(request) });
		}
	}
}
