using System.Threading;
using Discord;
﻿using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.Maps.Messages
{
	internal abstract class BaseLinkParsingMessageHandler : ICustomMessageHandler
	{
		protected abstract string[] RegexMatchPatterns { get; }

		protected virtual bool RegexMatchCase { get; } = false;

		private readonly RegexOptions _regexOptions;

		internal BaseLinkParsingMessageHandler()
		{
			_regexOptions = RegexMatchCase ? RegexOptions.Compiled : RegexOptions.Compiled | RegexOptions.IgnoreCase;
		}

		public bool CanHandle(IUserMessage message)
		{
			return RegexMatchPatterns.Any(regexMatchPattern => Regex.IsMatch(message.Content, regexMatchPattern, _regexOptions));
		}

		public abstract Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);

		protected IEnumerable<string> GetMatchedLinks(string message)
		{
			foreach (var regexMatchPattern in RegexMatchPatterns)
			{
				var matches = Regex.Matches(message, regexMatchPattern, _regexOptions);
				foreach (Match match in matches)
				{
					yield return match.Value;
				}
			}
		}
	}
}
