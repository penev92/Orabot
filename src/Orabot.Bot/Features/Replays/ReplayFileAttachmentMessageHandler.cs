using System.Threading;
using Discord;
﻿using System.Linq;
using System.Threading.Tasks;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.Replays
{
	internal class ReplayFileAttachmentMessageHandler : BaseAttachmentMessageHandler
	{
		protected override string FileExtension => ".orarep";

		private readonly AttachmentReplayMetadataTransformer _metadataTransformer;
		private readonly ReplayMetadataToEmbedTransformer _toEmbedTransformer;

		public ReplayFileAttachmentMessageHandler(AttachmentReplayMetadataTransformer metadataTransformer, ReplayMetadataToEmbedTransformer toEmbedTransformer)
		{
			_metadataTransformer = metadataTransformer;
			_toEmbedTransformer = toEmbedTransformer;
		}

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			var attachment = message.Attachments.First(x => x.Filename.EndsWith(FileExtension));

			var replayMetadata = _metadataTransformer.GetMetadata(attachment);
			replayMetadata.FileName = attachment.Filename;
			var embed = await _toEmbedTransformer.CreateEmbed(replayMetadata, attachment.Url);
			if (embed != null)
			{
				var replyMessage = $"{message.Author.Mention} posted a replay";
				if (string.IsNullOrWhiteSpace(message.Content))
				{
					replyMessage += ":";
				}
				else
				{
					replyMessage += $" with commentary:\n{message.Content}";
				}

				await message.Channel.SendMessageAsync(replyMessage, embed: embed);
			}
		}
	}
}
