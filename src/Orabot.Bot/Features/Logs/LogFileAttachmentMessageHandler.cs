using Discord;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Infrastructure.Attachments;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Logs
{
	internal sealed class LogFileAttachmentMessageHandler : BaseAttachmentMessageHandler
	{
		protected override string FileExtension => ".log";
		private readonly AttachmentLogFileToMessageTransformer _transformer;
		public LogFileAttachmentMessageHandler(AttachmentLogFileToMessageTransformer transformer) => _transformer = transformer;

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(AttachmentDownloader.OperationTimeout);
			var attachment = message.Attachments.First(item => item.Filename?.EndsWith(FileExtension, StringComparison.Ordinal) == true);
			var log = await _transformer.CreateRawMessageAsync(attachment, deadline.Token);
			if (string.IsNullOrWhiteSpace(log.RawMessage))
				return;
			var text = log.RawMessage;
			if (_transformer.TryCreateExceptionExplanationMessage(log.FullText, out var explanation, 2000 - text.Length - 2))
				text += "\r\n" + explanation;
			await message.Channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None,
				options: new RequestOptions { CancelToken = deadline.Token });
		}
	}
}
