using System;
using System.Threading;
using Discord;

namespace Orabot.Bot.Discord.Interactions
{
	/// <summary>Bounds an initial response by both a short transport budget and the original interaction window.</summary>
	internal static class InteractionResponseDeadline
	{
		public static CancellationTokenSource Create(IDiscordInteraction interaction, TimeProvider clock)
		{
			var remaining = interaction.CreatedAt + TimeSpan.FromMilliseconds(2800) - clock.GetUtcNow();
			var budget = remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2);
			if (budget <= TimeSpan.Zero)
				throw new TimeoutException("The interaction acknowledgment window has elapsed.");
			return new CancellationTokenSource(budget, clock);
		}
	}
}
