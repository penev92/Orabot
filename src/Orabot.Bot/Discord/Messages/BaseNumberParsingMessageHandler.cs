using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace Orabot.Bot.Discord.Messages
{
	internal abstract class BaseNumberParsingMessageHandler : ICustomMessageHandler
	{
		protected abstract Dictionary<string, int> MinimumHandledNumberPerKeyword { get; }
		protected virtual string[] RegexMatchPatternKeywords => MinimumHandledNumberPerKeyword.Keys.ToArray();
		protected virtual string RegexMatchPattern => "(^|[^a-zA-Z0-9])({keyword}#[0-9]+)";
		protected virtual bool RegexMatchCase => false;
		readonly Regex[] patterns;
		readonly Dictionary<string, int> minimums;

		protected BaseNumberParsingMessageHandler()
		{
			minimums = new Dictionary<string, int>(MinimumHandledNumberPerKeyword, RegexMatchCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
			patterns = RegexMatchPatternKeywords.Select(keyword => new Regex(RegexMatchPattern.Replace("{keyword}", Regex.Escape(keyword)),
				RegexOptions.Compiled | (RegexMatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(250))).ToArray();
		}

		public bool CanHandle(IUserMessage message) => GetMatchedNumbers(message.Content).Any();
		public abstract Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);

		protected IEnumerable<string> GetMatchedNumbers(string message) => patterns.SelectMany(pattern => pattern.Matches(message ?? string.Empty).Cast<Match>())
			.Select(match => match.Groups.Values.Last().Value.Split('#'))
			.Where(parts => parts.Length == 2 && int.TryParse(parts[1], out var number) && minimums.TryGetValue(parts[0], out var minimum) && number >= minimum)
			.Select(parts => parts[1]).Distinct(StringComparer.Ordinal).Take(3);
	}
}
