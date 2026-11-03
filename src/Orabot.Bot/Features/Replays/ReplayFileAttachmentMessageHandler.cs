using Discord;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Infrastructure.Attachments;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Replays
{
	internal sealed class ReplayFileAttachmentMessageHandler : BaseAttachmentMessageHandler
	{
		protected override string FileExtension => ".orarep";
		private readonly AttachmentReplayMetadataTransformer _metadata;
		private readonly ReplayMetadataToEmbedTransformer _renderer;
		public ReplayFileAttachmentMessageHandler(AttachmentReplayMetadataTransformer metadata, ReplayMetadataToEmbedTransformer renderer)
		{
			_metadata = metadata;
			_renderer = renderer;
		}

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(AttachmentDownloader.OperationTimeout);
			var attachment = message.Attachments.First(item => item.Filename?.EndsWith(FileExtension, StringComparison.Ordinal) == true);
			var metadata = await _metadata.GetMetadataAsync(attachment, deadline.Token);
			var embed = await _renderer.CreateEmbed(metadata, attachment.Url, deadline.Token);
			var text = $"{message.Author.Mention} posted a replay";
			text += string.IsNullOrWhiteSpace(message.Content) ? ":" : $" with commentary:\n{message.Content}";
			if (text.Length > 2000)
				text = text[..1997] + "...";
			await message.Channel.SendMessageAsync(text, embed: embed,
				allowedMentions: new AllowedMentions { AllowedTypes = AllowedMentionTypes.None, UserIds = new List<ulong> { message.Author.Id } },
				options: new RequestOptions { CancelToken = deadline.Token });
		}
	}
}
