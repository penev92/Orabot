using System.Threading;
using Discord;
﻿using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.Logs
{
	internal class StackTraceMessageHandler : ICustomMessageHandler
	{
		private readonly AttachmentLogFileToMessageTransformer _attachmentLogFileToMessageTransformer;

		public StackTraceMessageHandler(AttachmentLogFileToMessageTransformer attachmentLogFileToMessageTransformer)
		{
			_attachmentLogFileToMessageTransformer = attachmentLogFileToMessageTransformer;
		}

		public bool CanHandle(IUserMessage message)
		{
			return Regex.IsMatch(message.Content, "Exception of type `[a-zA-Z.]*`: ", RegexOptions.Compiled)
				|| Regex.IsMatch(message.Content, "Exception of type [a-zA-Z.]*: ", RegexOptions.Compiled);
		}

		public async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			if (_attachmentLogFileToMessageTransformer.TryCreateExceptionExplanationMessage(message.Content, out var explanationMessage))
				await message.Channel.SendMessageAsync(explanationMessage);
		}
	}
}
