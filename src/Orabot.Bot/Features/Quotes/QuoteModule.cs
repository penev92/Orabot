using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;

namespace Orabot.Bot.Features.Quotes
{
	/// <summary>Provides structured same-server message/range quotes and trusted custom quotations.</summary>
	[Group("quote", "Quotes source messages or trusted custom text.")]
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild)]
	public sealed class QuoteModule : InteractionModuleBase<IInteractionContext>
	{
		private readonly QuotingService _quotes;
		private readonly IHostApplicationLifetime _lifetime;

		/// <summary>Receives quote behavior and host cancellation; construction performs no requests.</summary>
		public QuoteModule(QuotingService quotes, IHostApplicationLifetime lifetime) { _quotes = quotes; _lifetime = lifetime; }

		/// <summary>Quotes one message or a same-author range identified by strings rather than lossy numeric options.</summary>
		[SlashCommand("message", "Quotes a message or same-author range from this server.")]
		public Task MessageAsync(
			[Summary("message", "Message ID, channel-ID-message-ID pair or Discord message link.")] string message,
			[Summary("end", "Optional final message identifier for a same-author range.")] string end = null,
			[Summary("channel", "Source channel; bare IDs otherwise search accessible server channels.")] ITextChannel channel = null)
			=> QuoteResponse.CompleteAsync(Context, _quotes.CreateMessageQuoteAsync(Context.Guild, Context.User,
				message, end, channel, Context.Channel as ITextChannel, _lifetime.ApplicationStopping), _lifetime.ApplicationStopping);

		/// <summary>Preserves free-form author/timestamp input under the existing trusted-role policy.</summary>
		[SlashCommand("custom", "Quotes custom text. Requires a configured trusted role.")]
		public Task CustomAsync(
			[Summary("text", "The text to quote."), MaxLength(4096)] string text,
			[Summary("author", "Optional displayed author name."), MaxLength(256)] string author = null,
			[Summary("channel", "Optional source channel in this server.")] ITextChannel channel = null,
			[Summary("timestamp", "Optional displayed timestamp, kept as free-form text."), MaxLength(256)] string timestamp = null)
			=> QuoteResponse.CompleteAsync(Context, _quotes.CreateCustomQuoteAsync(Context.Guild, Context.User,
				text, author, channel, timestamp, _lifetime.ApplicationStopping), _lifetime.ApplicationStopping);
	}

	/// <summary>Quotes a selected message through the same source authorization and rendering path.</summary>
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild)]
	public sealed class QuoteMessageContextModule : InteractionModuleBase<IInteractionContext>
	{
		private readonly QuotingService _quotes;
		private readonly IHostApplicationLifetime _lifetime;

		/// <summary>Uses the same concrete feature services as the slash command group.</summary>
		public QuoteMessageContextModule(QuotingService quotes, IHostApplicationLifetime lifetime) { _quotes = quotes; _lifetime = lifetime; }

		/// <summary>Re-fetches the selected message only after checking requester and bot access to its source.</summary>
		[MessageCommand("Quote message")]
		public Task QuoteAsync(IMessage message) => QuoteResponse.CompleteAsync(Context,
			_quotes.CreateMessageQuoteAsync(Context.Guild, Context.User, $"{message.Channel.Id}-{message.Id}",
				cancellationToken: _lifetime.ApplicationStopping), _lifetime.ApplicationStopping);
	}

	internal static class QuoteResponse
	{
		internal static async Task CompleteAsync(IInteractionContext context, Task<Embed> quote, CancellationToken cancellationToken)
		{
			Embed embed = null;
			string content;
			try
			{
				embed = await quote;
				content = context.User.Username + ":";
			}
			catch (ArgumentException)
			{
				content = "Check the quote options. Messages and channels must belong to this server; custom text allows 4096 characters and author/timestamp allow 256 each.";
			}
			catch (UnauthorizedAccessException)
			{
				content = "You and Orabot must be able to view the source channel and read its history. Custom quotes also require a configured trusted role.";
			}
			await context.Interaction.ModifyOriginalResponseAsync(properties =>
			{
				properties.Content = content;
				properties.Embeds = embed == null ? Array.Empty<Embed>() : new[] { embed };
				properties.AllowedMentions = AllowedMentions.None;
			}, new RequestOptions { CancelToken = cancellationToken });
		}
	}
}
