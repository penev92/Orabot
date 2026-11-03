using System.Threading;
using Discord;
﻿using Orabot.Bot.Features.Maps.Rendering;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Maps.Messages
{
	internal class OpenRaResourceCenterMapLinkMessageHandler : BaseLinkParsingMessageHandler
	{
		protected override string[] RegexMatchPatterns { get; } = { "https://resource.openra.net/maps/[0-9]+" };

		private readonly OpenRaResourceCenterMapLinkToEmbedTransformer _toEmbedTransformer;

		public OpenRaResourceCenterMapLinkMessageHandler(OpenRaResourceCenterMapLinkToEmbedTransformer toEmbedTransformer)
		{
			_toEmbedTransformer = toEmbedTransformer;
		}

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			foreach (var matchedLink in GetMatchedLinks(message.Content))
			{
				var numberStr = matchedLink[(matchedLink.LastIndexOf('/') + 1)..];
				if (!int.TryParse(numberStr, out var number))
				{
					continue;
				}

				var embed = await _toEmbedTransformer.CreateEmbed(number);
				if (embed != null)
					await message.Channel.SendMessageAsync("", embed: embed);
			}
		}
	}
}
