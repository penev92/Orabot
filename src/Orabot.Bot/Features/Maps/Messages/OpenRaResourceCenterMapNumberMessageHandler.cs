using System.Threading;
using Discord;
﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Features.Maps.Rendering;

namespace Orabot.Bot.Features.Maps.Messages
{
	internal class OpenRaResourceCenterMapNumberMessageHandler : BaseNumberParsingMessageHandler
	{
		protected override Dictionary<string, int> MinimumHandledNumberPerKeyword { get; } = new Dictionary<string, int>
		{
			{ "map", 0 }
		};

		private readonly OpenRaResourceCenterMapLinkToEmbedTransformer _toEmbedTransformer;

		public OpenRaResourceCenterMapNumberMessageHandler(OpenRaResourceCenterMapLinkToEmbedTransformer toEmbedTransformer)
		{
			_toEmbedTransformer = toEmbedTransformer;
		}

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			foreach (var numberStr in GetMatchedNumbers(message.Content))
			{
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
