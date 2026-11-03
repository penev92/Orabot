using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Discord;

namespace Orabot.Bot.Features.Quotes
{
	/// <summary>Formats authorized source data and trusted custom text without network or member enumeration.</summary>
	internal static class QuoteEmbedRenderer
	{
		internal static Embed CreateSourceEmbed(ulong guildId, ITextChannel channel, IReadOnlyList<IMessage> messages)
		{
			var first = messages[0];
			var author = first.Author;
			var authorName = (author as IGuildUser)?.Nickname ?? author.Username;
			var jump = $"https://discord.com/channels/{guildId}/{channel.Id}/{first.Id}";
			var suffix = $"\n\n[Original message]({jump})";
			var body = string.Join("\n", messages.Select(message => message.Content));
			var imageUrl = first.Attachments.FirstOrDefault()?.Url;
			if (!IsHttpUrl(imageUrl))
				imageUrl = IsHttpUrl(first.Content) ? first.Content : null;
			return new EmbedBuilder
			{
				Color = Color.Blue,
				Author = new EmbedAuthorBuilder { Name = Clip(authorName ?? "Unknown author", 256), IconUrl = author.GetAvatarUrl() },
				Description = Clip(body, 4096 - suffix.Length) + suffix,
				Footer = new EmbedFooterBuilder { Text = first.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC, in #" + channel.Name },
				ImageUrl = imageUrl
			}.Build();
		}

		internal static Embed CreateCustomEmbed(string text, string author, ITextChannel channel, string timestamp)
		{
			var footer = string.Join(", ", new[] { timestamp, channel == null ? null : "in #" + channel.Name }.Where(value => !string.IsNullOrEmpty(value)));
			return new EmbedBuilder
			{
				Color = Color.Blue,
				Author = string.IsNullOrEmpty(author) ? null : new EmbedAuthorBuilder { Name = author },
				Description = text,
				Footer = footer.Length == 0 ? null : new EmbedFooterBuilder { Text = Clip(footer, 2048) }
			}.Build();
		}

		private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
			&& uri.Scheme is "https" or "http" && uri.Host.Length != 0 && uri.UserInfo.Length == 0;

		private static string Clip(string text, int limit) => text.Length <= limit ? text : text.Substring(0, limit - 3) + "...";
	}
}
