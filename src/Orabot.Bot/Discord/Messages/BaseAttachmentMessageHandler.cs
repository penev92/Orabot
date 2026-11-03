using Discord;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Discord.Messages
{
	internal abstract class BaseAttachmentMessageHandler : ICustomMessageHandler
	{
		protected abstract string FileExtension { get; }

		public virtual bool CanHandle(IUserMessage message) => message.Attachments.Any(attachment =>
			attachment.Filename?.EndsWith(FileExtension, StringComparison.Ordinal) == true);

		public abstract Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);
	}
}
