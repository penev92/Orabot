using Discord;
using Orabot.Bot.Features.Maps.Rendering;
using Orabot.Bot.Features.Replays.Models;
using Orabot.Bot.Infrastructure.Attachments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Replays
{
	internal sealed class ReplayMetadataToEmbedTransformer
	{
		private readonly Func<string, CancellationToken, Task<Embed>> _mapLookup;
		public ReplayMetadataToEmbedTransformer(OpenRaResourceCenterMapLinkToEmbedTransformer maps)
			: this((uid, token) => maps.CreateEmbed(uid, token)) { }
		internal ReplayMetadataToEmbedTransformer(Func<string, CancellationToken, Task<Embed>> mapLookup) => _mapLookup = mapLookup;

		internal async Task<Embed> CreateEmbed(ReplayMetadata metadata, string replayLink = null, CancellationToken cancellationToken = default)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(AttachmentDownloader.OperationTimeout);
			var map = await _mapLookup(metadata.MapUid, deadline.Token);
			deadline.Token.ThrowIfCancellationRequested();
			var players = metadata.Players.Values.Take(AttachmentReplayMetadataTransformer.MaximumPlayers).ToArray();
			var teams = players.GroupBy(player => player.Team).OrderBy(group => group.Key).ToArray();
			var counts = teams.SelectMany(group => group.Key == 0 ? group.Select(_ => "1") : new[] { group.Count().ToString() });
			var winners = players.Where(player => player.Outcome == "Won").ToArray();
			var winner = winners.Length switch
			{
				0 => "Winner is unknown.",
				1 => $"Winner is {winners[0].Name}",
				_ => $"Winners are {string.Join(", ", winners.Select(player => player.Name))}"
			};
			var title = Clip(metadata.FileName, 256);
			var description = Clip($"This is a {string.Join("vs", counts)} game. || {winner} ||", 1024);
			var footer = $"Played {metadata.StartTimeUtc}";
			var fields = new List<EmbedFieldBuilder>();
			var remaining = 6000 - title.Length - description.Length - footer.Length;
			void Add(string name, string value, bool inline)
			{
				if (fields.Count >= 25 || remaining <= name.Length)
					return;
				var text = Clip(value, Math.Min(1024, remaining - name.Length));
				fields.Add(new EmbedFieldBuilder { Name = name, Value = text, IsInline = inline });
				remaining -= name.Length + text.Length;
			}
			Add("Mod:", metadata.Mod, true);
			Add("Version:", metadata.Version, true);
			Add("Map:", map == null ? metadata.MapTitle : $"[{metadata.MapTitle}]({map.Url})", true);
			Add("Duration:", $"||{metadata.EndTimeUtc - metadata.StartTimeUtc}||", false);
			foreach (var group in teams)
				Add(group.Key == 0 ? "No team:" : $"Team {group.Key}", string.Join("\n", group), true);
			return new EmbedBuilder
			{
				Title = title, Url = replayLink, Description = description,
				ThumbnailUrl = map?.Thumbnail?.Url,
				Footer = new EmbedFooterBuilder { Text = footer }, Color = map?.Color ?? Color.Default, Fields = fields
			}.Build();
		}

		private static string Clip(string value, int limit)
		{
			value = string.IsNullOrEmpty(value) ? "Unknown" : value;
			return value.Length <= limit ? value : limit <= 3 ? value[..limit] : value[..(limit - 3)] + "...";
		}
	}
}
