using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using System.Threading;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.General
{
	internal class BaseModTimerMessageHandler : ICustomMessageHandler
	{
		private readonly string _regexMatchPattern = "\\b(\\w*[a-zA-Z]\\w*)\\b";
		private readonly string[] _modIdentifiers = { "ts", "ra2" };
		private readonly string[] _keywords = { "when", "status", "release", "public", "available" };

		public bool CanHandle(IUserMessage message)
		{
			var words = Regex.Matches(message.Content, _regexMatchPattern, RegexOptions.None, System.TimeSpan.FromMilliseconds(250)).Select(x => x.Value.ToLower()).ToArray();
			return _modIdentifiers.Any(x => words.Contains(x)) && _keywords.Any(x => words.Contains(x)) && message.Content.Contains('?');
		}

		public async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			await message.Channel.SendMessageAsync("Timer reset. 7 months remaining.", allowedMentions: AllowedMentions.None, options: new RequestOptions { CancelToken = cancellationToken });
		}
	}
}
