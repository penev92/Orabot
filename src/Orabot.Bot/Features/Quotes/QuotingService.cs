using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Net;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.General;

namespace Orabot.Bot.Features.Quotes
{
	/// <summary>Resolves same-guild quote sources with requester/bot authorization and bounded asynchronous reads.</summary>
	public sealed class QuotingService
	{
		internal const int MaximumSearchChannels = 50;
		private readonly TrustedRoleOptions _trustedRoles;

		/// <summary>Uses the existing role-name allowlist for user-authored custom quotations.</summary>
		public QuotingService(IOptions<TrustedRoleOptions> trustedRoles) => _trustedRoles = trustedRoles.Value;

		/// <summary>Builds one quote or a same-author range, using at most fifty channels and twenty-five following messages.</summary>
		public async Task<Embed> CreateMessageQuoteAsync(IGuild guild, IUser requester, string message,
			string end = null, ITextChannel channel = null, ITextChannel currentChannel = null, CancellationToken cancellationToken = default)
		{
			if (guild == null || requester == null)
				throw new ArgumentException("Message quotes can only be used in a server.");
			if (!DiscordMessageIdentifier.TryParse(message, out var first))
				throw new ArgumentException("Use a message ID, channel-ID-message-ID pair or Discord message link.");
			DiscordMessageIdentifier last = null;
			if (end != null && !DiscordMessageIdentifier.TryParse(end, out last))
				throw new ArgumentException("The end message identifier is invalid.");
			ValidateGuild(first, guild);
			if (last != null)
			{
				ValidateGuild(last, guild);
				if (last.MessageId < first.MessageId)
					throw new ArgumentException("The end message must not precede the first message.");
			}
			if (channel != null && (channel.GuildId != guild.Id || (first.ChannelId.HasValue && first.ChannelId.Value != channel.Id)))
				throw new ArgumentException("Choose a source channel in this server that matches the message identifier.");

			using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			budget.CancelAfter(TimeSpan.FromSeconds(10));
			var token = budget.Token;
			token.ThrowIfCancellationRequested();
			var options = new RequestOptions { CancelToken = token };
			var invoker = await ResolveRequesterAsync(guild, requester, options);
			var bot = await guild.GetCurrentUserAsync(CacheMode.AllowDownload, options);
			if (bot == null || bot.GuildId != guild.Id)
				throw new UnauthorizedAccessException("The quote source is unavailable or its history cannot be read.");

			ITextChannel source;
			IMessage firstMessage;
			if (first.ChannelId.HasValue || channel != null)
			{
				source = channel ?? await guild.GetTextChannelAsync(first.ChannelId.Value, CacheMode.AllowDownload, options);
				RequireReadable(source, guild, invoker, bot);
				firstMessage = await ReadMessageAsync(source, first.MessageId, options);
			}
			else
			{
				(source, firstMessage) = await FindMessageAsync(guild, invoker, bot, first.MessageId, currentChannel, options);
			}
			if (firstMessage == null || source == null)
				throw new ArgumentException("No such message was found in the accessible channels searched in this server.");
			if (firstMessage.Id != first.MessageId)
				throw new ArgumentException("The source returned an unexpected message.");
			if (last?.ChannelId != null && last.ChannelId.Value != source.Id)
				throw new ArgumentException("Both range endpoints must belong to the same source channel.");

			var messages = new List<IMessage> { firstMessage };
			if (last != null && last.MessageId != first.MessageId)
			{
				// The existing range budget is the first message plus at most 25 following messages.
				var following = new List<IMessage>();
				RequireReadable(source, guild, invoker, bot);
				await foreach (var page in source.GetMessagesAsync(first.MessageId, Direction.After, 25, CacheMode.AllowDownload, options).WithCancellation(token))
				{
					token.ThrowIfCancellationRequested();
					following.AddRange(page.Take(25));
					break;
				}
				foreach (var item in following.Where(item => item.Id > first.MessageId && item.Id <= last.MessageId).OrderBy(item => item.Id))
					if (item.Author.Id == firstMessage.Author.Id)
						messages.Add(item);
			}
			// Recheck permissions after awaited reads, before presenting retrieved content elsewhere.
			RequireReadable(source, guild, invoker, bot);
			return QuoteEmbedRenderer.CreateSourceEmbed(guild.Id, source, messages);
		}

		/// <summary>Builds trusted custom text with explicit author, source channel and free-form timestamp options.</summary>
		public async Task<Embed> CreateCustomQuoteAsync(IGuild guild, IUser requester, string text, string author = null,
			ITextChannel channel = null, string timestamp = null, CancellationToken cancellationToken = default)
		{
			if (guild == null || requester == null)
				throw new ArgumentException("Custom quotes can only be used in a server.");
			if (string.IsNullOrWhiteSpace(text) || text.Length > 4096 || (author?.Length ?? 0) > 256 || (timestamp?.Length ?? 0) > 256)
				throw new ArgumentException("Quote text must contain 1–4096 characters; author and timestamp allow up to 256 characters each.");
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			budget.CancelAfter(TimeSpan.FromSeconds(10));
			var token = budget.Token;
			token.ThrowIfCancellationRequested();
			var options = new RequestOptions { CancelToken = token };
			var invoker = await ResolveRequesterAsync(guild, requester, options);
			if (!_trustedRoles.IsTrusted(invoker, guild))
				throw new UnauthorizedAccessException("No rights! Custom quotes require a configured trusted role.");
			if (channel != null)
			{
				var bot = await guild.GetCurrentUserAsync(CacheMode.AllowDownload, options);
				RequireReadable(channel, guild, invoker, bot);
			}
			return QuoteEmbedRenderer.CreateCustomEmbed(text, author, channel, timestamp);
		}

		private static void ValidateGuild(DiscordMessageIdentifier identifier, IGuild guild)
		{
			if (identifier.GuildId.HasValue && identifier.GuildId.Value != guild.Id)
				throw new ArgumentException("Cross-server quotes are not supported. Choose a message from this server.");
		}

		private static async Task<IGuildUser> ResolveRequesterAsync(IGuild guild, IUser requester, RequestOptions options)
		{
			var member = requester as IGuildUser;
			if (member?.GuildId != guild.Id)
				member = await guild.GetUserAsync(requester.Id, CacheMode.AllowDownload, options);
			if (member == null || member.GuildId != guild.Id)
				throw new UnauthorizedAccessException("Your membership in the source server could not be verified.");
			return member;
		}

		private static bool CanRead(ITextChannel channel, IGuild guild, IGuildUser requester, IGuildUser bot)
		{
			// Parent channel permissions do not establish private-thread membership.
			if (channel == null || channel is IThreadChannel { Type: ThreadType.PrivateThread }
				|| channel.GuildId != guild.Id || requester == null || bot == null
				|| requester.GuildId != guild.Id || bot.GuildId != guild.Id)
				return false;
			var userPermissions = requester.GetPermissions(channel);
			var botPermissions = bot.GetPermissions(channel);
			return userPermissions.ViewChannel && userPermissions.ReadMessageHistory
				&& botPermissions.ViewChannel && botPermissions.ReadMessageHistory;
		}

		private static void RequireReadable(ITextChannel channel, IGuild guild, IGuildUser requester, IGuildUser bot)
		{
			if (!CanRead(channel, guild, requester, bot))
				throw new UnauthorizedAccessException("The quote source is unavailable or its history cannot be read by both you and Orabot.");
		}

		private static async Task<(ITextChannel Channel, IMessage Message)> FindMessageAsync(IGuild guild, IGuildUser requester,
			IGuildUser bot, ulong messageId, ITextChannel preferred, RequestOptions options)
		{
			var searched = new HashSet<ulong>();
			if (preferred != null && CanRead(preferred, guild, requester, bot))
			{
				searched.Add(preferred.Id);
				var preferredMessage = await ReadMessageAsync(preferred, messageId, options);
				if (preferredMessage != null)
					return (preferred, preferredMessage);
			}
			var channels = await guild.GetTextChannelsAsync(CacheMode.AllowDownload, options);
			foreach (var candidate in channels.Where(item => item != null).DistinctBy(item => item.Id)
				.Where(item => !searched.Contains(item.Id) && CanRead(item, guild, requester, bot))
				.Take(MaximumSearchChannels - searched.Count))
			{
				options.CancelToken.ThrowIfCancellationRequested();
				var found = await ReadMessageAsync(candidate, messageId, options);
				if (found != null)
					return (candidate, found);
			}
			return (null, null);
		}

		private static async Task<IMessage> ReadMessageAsync(ITextChannel channel, ulong id, RequestOptions options)
		{
			try { return await channel.GetMessageAsync(id, CacheMode.AllowDownload, options); }
			catch (HttpException exception) when (exception.HttpCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden) { return null; }
		}
	}
}
