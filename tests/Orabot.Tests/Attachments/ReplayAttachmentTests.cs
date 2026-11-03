using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Replays;
using Orabot.Bot.Features.Replays.Models;
using Orabot.Bot.Infrastructure.Attachments;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;

namespace Orabot.Tests.Attachments;

/// <summary>Checks OpenRA's real footer layout, finite metadata parsing and replay presentation using synthetic files.</summary>
public sealed class ReplayAttachmentTests
{
	[Fact]
	public async Task Valid_metadata_is_decoded_and_complete_replay_is_archived()
	{
		using var files = new AttachmentFiles();
		var body = ReplayBytes(NormalYaml);
		using var http = new AttachmentHttp(body);
		var metadata = await Transformer(http, files).GetMetadataAsync(AttachmentFixtures.Attachment(body.Length, "match.orarep"));
		Assert.Equal("match.orarep", metadata.FileName);
		Assert.Equal("ra", metadata.Mod);
		Assert.Equal("DEV_VERSION", metadata.Version);
		Assert.Equal("Fixture Map", metadata.MapTitle);
		Assert.Equal(2, metadata.Players.Count);
		Assert.Equal("Alice [Allies]", metadata.Players[0].ToString());
		Assert.Equal(TimeSpan.FromMinutes(5), metadata.EndTimeUtc - metadata.StartTimeUtc);
		Assert.Equal(body, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(files.Root))));
	}

	[Theory]
	[InlineData("start")]
	[InlineData("end")]
	[InlineData("version")]
	[InlineData("negative")]
	[InlineData("oversized")]
	[InlineData("outside")]
	[InlineData("string-length")]
	[InlineData("utf8")]
	[InlineData("truncated")]
	public async Task Corrupt_footer_or_string_metadata_is_rejected_without_exposing_uploaded_content(string mutation)
	{
		var body = ReplayBytes(NormalYaml);
		switch (mutation)
		{
			case "start": Write(8, 0); break;
			case "end": Write(body.Length - 4, 0); break;
			case "version": Write(12, 2); break;
			case "negative": Write(body.Length - 8, -1); break;
			case "oversized": Write(body.Length - 8, AttachmentReplayMetadataTransformer.MaximumMetadataBytes + 5); break;
			case "outside": Write(body.Length - 8, body.Length + 100); break;
			case "string-length": Write(16, 1); break;
			case "utf8": body[20] = 255; break;
			case "truncated": body = body[..^1]; break;
		}
		await using var stream = new MemoryStream(body);
		var error = await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentReplayMetadataTransformer.ReadMetadataAsync(stream));
		Assert.DoesNotContain("Alice", error.Message);
		Assert.Null(error.InnerException);
		void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(offset, 4), value);
	}

	[Fact]
	public async Task Too_short_file_and_over_budget_metadata_are_rejected_before_deserialization()
	{
		await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentReplayMetadataTransformer.ReadMetadataAsync(new MemoryStream(new byte[19])));
		var body = ReplayBytes(new string('x', AttachmentReplayMetadataTransformer.MaximumMetadataBytes + 1));
		await Assert.ThrowsAsync<InvalidDataException>(() => AttachmentReplayMetadataTransformer.ReadMetadataAsync(new MemoryStream(body)));
	}

	[Theory]
	[InlineData("anchors")]
	[InlineData("depth")]
	[InlineData("scalar")]
	[InlineData("duplicate-player")]
	[InlineData("broken-yaml")]
	[InlineData("players")]
	[InlineData("events")]
	public async Task Unsafe_or_corrupt_yaml_is_bounded_and_reports_only_a_safe_category(string kind)
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(Array.Empty<byte>());
		var yaml = kind switch
		{
			"anchors" => NormalYaml.Replace("Root:", "Root: &private-anchor") + "\nAlias: *private-anchor\n",
			"depth" => NormalYaml + "\nDeep:\n" + string.Join("\n", Enumerable.Range(1, 40).Select(depth => new string(' ', depth * 2) + "Next:")) + " value\n",
			"scalar" => NormalYaml + "\nUnknown: " + new string('x', 8193),
			"duplicate-player" => NormalYaml.Replace("ClientIndex: 1", "ClientIndex: 0"),
			"players" => NormalYaml.Split("Player@0:")[0] + string.Join("\n", Enumerable.Range(0, 65).Select(index =>
				$"Player@{index}:\n  ClientIndex: {index}\n  Name: Player{index}\n  FactionName: Allies\n  DisplayFactionName: Allies\n")),
			"events" => NormalYaml + "\nAdditional:\n" + string.Join("\n", Enumerable.Range(0, 10000).Select(index => "  Key" + index + ": value")),
			_ => "Root:\n  private-payload: [\n"
		};
		await using var stream = new MemoryStream(ReplayBytes(yaml));
		var error = await Assert.ThrowsAsync<InvalidDataException>(() => Transformer(http, files).ParseAsync(stream, "fixture.orarep"));
		Assert.DoesNotContain("private", error.Message);
		Assert.Null(error.InnerException);
	}

	[Fact]
	public async Task Replay_embed_keeps_normal_fields_and_map_lookup_observes_cancellation()
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(Array.Empty<byte>());
		var metadata = await Transformer(http, files).ParseAsync(new MemoryStream(ReplayBytes(NormalYaml)), "match.orarep");
		var renderer = new ReplayMetadataToEmbedTransformer((_, _) => Task.FromResult<Embed>(null!));
		var embed = await renderer.CreateEmbed(metadata);
		Assert.Equal("match.orarep", embed.Title);
		Assert.Equal("This is a 1vs1 game. || Winner is Alice ||", embed.Description);
		Assert.Equal("Fixture Map", embed.Fields.Single(field => field.Name == "Map:").Value);
		Assert.Equal("Alice [Allies]", embed.Fields.Single(field => field.Name == "Team 1").Value);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken received = default;
		var waiting = new ReplayMetadataToEmbedTransformer(async (_, token) =>
		{
			received = token; entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, token);
			return null!;
		});
		using var cancellation = new CancellationTokenSource();
		var lookup = waiting.CreateEmbed(metadata, cancellationToken: cancellation.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.True(received.IsCancellationRequested);
	}

	[Fact]
	public async Task Replay_handler_keeps_commentary_and_only_allows_the_intended_author_mention()
	{
		using var files = new AttachmentFiles();
		var body = ReplayBytes(NormalYaml);
		using var http = new AttachmentHttp(body);
		string? sent = null;
		Embed? sentEmbed = null;
		AllowedMentions? mentions = null;
		RequestOptions? options = null;
		var message = AttachmentFixtures.Message(AttachmentFixtures.Attachment(body.Length, "match.orarep"), "A good game",
			(text, embed, allowed, request) => { sent = text; sentEmbed = embed; mentions = allowed; options = request; });
		var handler = new ReplayFileAttachmentMessageHandler(Transformer(http, files),
			new ReplayMetadataToEmbedTransformer((_, _) => Task.FromResult<Embed>(null!)));

		await handler.InvokeAsync(message);

		Assert.Equal("<@123> posted a replay with commentary:\nA good game", sent);
		Assert.NotNull(sentEmbed);
		Assert.NotNull(mentions);
		Assert.Equal(new[] { 123UL }, mentions.UserIds);
		Assert.Equal((AllowedMentionTypes)0, mentions.AllowedTypes);
		Assert.NotNull(options);
		Assert.True(options.CancelToken.CanBeCanceled);
		Assert.Single(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Renderer_respects_per_field_and_total_discord_embed_budgets()
	{
		var metadata = new ReplayMetadata
		{
			FileName = new string('x', 1000), Mod = new string('m', 2000), Version = new string('v', 2000),
			MapTitle = new string('t', 4000), MapUid = "fixture", StartTimeUtc = DateTime.UtcNow, EndTimeUtc = DateTime.UtcNow,
			Players = Enumerable.Range(0, 64).ToDictionary(index => index, index => new PlayerData
			{
				Name = new string('n', 256), FactionName = new string('f', 256), DisplayFactionName = new string('d', 256),
				Team = index, Outcome = "Won"
			})
		};
		var embed = await new ReplayMetadataToEmbedTransformer((_, _) => Task.FromResult<Embed>(null!)).CreateEmbed(metadata);
		Assert.InRange(embed.Title.Length, 1, 256);
		Assert.InRange(embed.Fields.Length, 4, 25);
		Assert.All(embed.Fields, field => Assert.InRange(field.Value.Length, 1, 1024));
		var length = embed.Title.Length + embed.Description.Length + embed.Footer!.Value.Text.Length
			+ embed.Fields.Sum(field => field.Name.Length + field.Value.Length);
		Assert.InRange(length, 1, 6000);
	}

	internal static byte[] ReplayBytes(string yaml)
	{
		var payload = Encoding.UTF8.GetBytes(yaml);
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
		writer.Write(new byte[8]);
		writer.Write(-1); writer.Write(1); writer.Write(payload.Length); writer.Write(payload);
		writer.Write(payload.Length + 4); writer.Write(-2); writer.Flush();
		return stream.ToArray();
	}

	static AttachmentReplayMetadataTransformer Transformer(AttachmentHttp http, AttachmentFiles files) => new(
		new AttachmentDownloader(http), Options.Create(new ReplayOptions { ReplayStorageFolder = files.Root }),
		new DeserializerBuilder().IgnoreUnmatchedProperties().WithDuplicateKeyChecking()
			.WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc, CultureInfo.InvariantCulture, false, "yyyy-MM-dd HH-mm-ss")).Build());

	const string NormalYaml = "Root:\n\tMod: ra\n\tVersion: {DEV_VERSION}\n\tMapUid: fixture-uid\n\tMapTitle: Fixture Map\n\tFinalGameTick: 100\n\tStartTimeUtc: 2026-01-01 10-00-00\n\tEndTimeUtc: 2026-01-01 10-05-00\n" +
		"Player@0:\n\tClientIndex: 0\n\tName: Alice\n\tFactionName: Allies\n\tDisplayFactionName: Allies\n\tTeam: 1\n\tOutcome: Won\n" +
		"Player@1:\n\tClientIndex: 1\n\tName: Bob\n\tFactionName: Soviets\n\tDisplayFactionName: Soviets\n\tTeam: 2\n\tOutcome: Lost\n";
}
