using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.Maps.Messages
{
	internal abstract class BaseLinkParsingMessageHandler : ICustomMessageHandler
	{
		protected abstract string[] RegexMatchPatterns { get; }
		protected virtual bool RegexMatchCase => false;
		readonly Regex[] patterns;

		protected BaseLinkParsingMessageHandler() => patterns = RegexMatchPatterns.Select(pattern => new Regex(pattern,
			RegexOptions.Compiled | (RegexMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(250))).ToArray();

		public bool CanHandle(IUserMessage message) => GetMatchedLinks(message.Content).Any();
		public abstract Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);
		protected IEnumerable<string> GetMatchedLinks(string message) => patterns.SelectMany(pattern => pattern.Matches(message ?? string.Empty).Cast<Match>())
			.Select(match => match.Value).Distinct(StringComparer.Ordinal).Take(3);
	}
}
