using System.Threading;
using Discord;
﻿using System.Linq;
using System.Threading.Tasks;

namespace Orabot.Bot.Discord.Messages
{
	internal abstract class BaseAttachmentMessageHandler : ICustomMessageHandler
	{
		protected abstract string FileExtension { get; }

		public virtual bool CanHandle(IUserMessage message)
		{
			return message.Attachments.Count != 0 && message.Attachments.Any(x => x.Filename.EndsWith(FileExtension));
		}

		public abstract Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);
	}
}
