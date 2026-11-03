using Discord;
using Orabot.Bot.Discord.Messages;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Logs
{
	internal sealed class StackTraceMessageHandler : ICustomMessageHandler
	{
		private static readonly Regex Pattern = new("Exception of type (?:`[a-zA-Z.]*`|[a-zA-Z.]*): ",
			RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
		private readonly AttachmentLogFileToMessageTransformer _transformer;
		public StackTraceMessageHandler(AttachmentLogFileToMessageTransformer transformer) => _transformer = transformer;
		public bool CanHandle(IUserMessage message) => !string.IsNullOrEmpty(message.Content) && Pattern.IsMatch(message.Content);

		public async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			if (_transformer.TryCreateExceptionExplanationMessage(message.Content, out var explanation))
				await message.Channel.SendMessageAsync(explanation, allowedMentions: AllowedMentions.None,
					options: new RequestOptions { CancelToken = cancellationToken });
		}
	}
}
