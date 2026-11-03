using System.Globalization;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Orabot.Bot.Features.Maps.Models;
using RestSharp;

namespace Orabot.Bot.Features.Maps.Rendering
{
	internal class OpenRaResourceCenterMapLinkToEmbedTransformer
	{
		private const string BaseUrl = "https://resource.openra.net";
		private const string ApiMapInfoByUidTemplate = "map/hash/{uid}";
		private const string ApiMapInfoByNumberTemplate = "map/id/{number}";

		private readonly IRestClient _restClient;

		public OpenRaResourceCenterMapLinkToEmbedTransformer(IRestClient restClient)
		{
			_restClient = restClient;
		}

		internal async Task<Embed> CreateEmbed(string mapUid, CancellationToken cancellationToken = default)
		{
			var request = new RestRequest($"{BaseUrl}/{ApiMapInfoByUidTemplate}");
			request.AddUrlSegment("uid", mapUid);

			var response = await _restClient.GetAsync<List<MapInfo>>(request, cancellationToken);
			var mapInfo = response?.FirstOrDefault();

			return await CreateEmbedInner(mapInfo, cancellationToken);
		}

		internal async Task<Embed> CreateEmbed(int number, CancellationToken cancellationToken = default)
		{
			var request = new RestRequest($"{BaseUrl}/{ApiMapInfoByNumberTemplate}");
			request.AddUrlSegment("number", number);

			var response = await _restClient.GetAsync<List<MapInfo>>(request, cancellationToken);
			var mapInfo = response?.FirstOrDefault();

			return await CreateEmbedInner(mapInfo, cancellationToken);
		}

		internal async Task<Embed> CreateEmbed(MapInfo mapInfo, CancellationToken cancellationToken = default)
		{
			return await CreateEmbedInner(mapInfo, cancellationToken);
		}

		#region Private methods

		private async Task<Embed> CreateEmbedInner(MapInfo mapInfo, CancellationToken cancellationToken)
		{
			if (mapInfo?.Title == null || mapInfo.Author == null)
				return null;

			var bounds = (mapInfo.Bounds ?? string.Empty).Split(',');
			if (bounds.Length != 4 || !int.TryParse(bounds[2], out var width) || !int.TryParse(bounds[3], out var height) || width <= 0 || height <= 0)
				return null;
			var size = $"{width}x{height}";
			var color = await GetColor($"mod_{mapInfo.GameMod}", cancellationToken);
			var number = mapInfo.Id;

			var url = $"{BaseUrl}/maps/{number}";
			var info = mapInfo.Info ?? string.Empty;
			var description = info.Length > 250 ? info[..250] + "..." : info;
			var authorUrl = $"{BaseUrl}/maps/author/{Uri.EscapeDataString(mapInfo.Author)}/";
			var minimapUrl = $"{BaseUrl}/maps/{number}/minimap";

			var title = $"{mapInfo.Title}\n({(mapInfo.GameMod ?? string.Empty).ToUpperInvariant()}, {mapInfo.Players} players, {size})";
			var embed = new EmbedBuilder
			{
				Title = title.Length > 256 ? title[..256] : title,
				ThumbnailUrl = minimapUrl,
				Url = url,
				Description = description,
				Author = new EmbedAuthorBuilder
				{
					Name = mapInfo.Author.Length > 256 ? mapInfo.Author[..256] : mapInfo.Author,
					Url = authorUrl
				},
				Footer = new EmbedFooterBuilder
				{
					Text = $"Published on {mapInfo.PostedOn}"
				},
				Color = color
			};

			return embed.Build();
		}

		private async Task<Color?> GetColor(string modIdentifier, CancellationToken cancellationToken)
		{
			var stylesheetLink = $"{BaseUrl}/static/style003.css";

			var request = new RestRequest(stylesheetLink);
			var response = await _restClient.GetAsync(request, cancellationToken);

			var match = Regex.Match(response.Content ?? string.Empty, Regex.Escape(modIdentifier) + @"[^}]*#(?<hex>[0-9a-fA-F]{6})",
				RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));
			return match.Success && uint.TryParse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color)
				? new Color(color) : null;
		}

		#endregion
	}
}
