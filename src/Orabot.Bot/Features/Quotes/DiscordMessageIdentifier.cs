using System;
using System.Globalization;
using System.Linq;

namespace Orabot.Bot.Features.Quotes
{
	/// <summary>A parsed message ID, optional channel ID and optional Discord message-link guild ID.</summary>
	public sealed record DiscordMessageIdentifier(ulong MessageId, ulong? ChannelId = null, ulong? GuildId = null)
	{
		/// <summary>Accepts bare IDs, channel-ID-message-ID pairs and canonical HTTPS Discord message links.</summary>
		public static bool TryParse(string input, out DiscordMessageIdentifier identifier)
		{
			identifier = null;
			if (string.IsNullOrWhiteSpace(input))
				return false;
			input = input.Trim();
			if (TryId(input, out var messageId))
			{
				identifier = new DiscordMessageIdentifier(messageId);
				return true;
			}
			var pair = input.Split('-');
			if (pair.Length == 2 && TryId(pair[0], out var channelId) && TryId(pair[1], out messageId))
			{
				identifier = new DiscordMessageIdentifier(messageId, channelId);
				return true;
			}
			if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
				|| !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
				|| !IsDiscordHost(uri.Host))
				return false;
			var parts = uri.AbsolutePath.TrimEnd('/').Split('/');
			if (parts.Length != 5 || parts[0] != "" || parts[1] != "channels"
				|| !TryId(parts[2], out var guildId) || !TryId(parts[3], out channelId) || !TryId(parts[4], out messageId))
				return false;
			identifier = new DiscordMessageIdentifier(messageId, channelId, guildId);
			return true;
		}

		private static bool TryId(string value, out ulong id)
		{
			id = 0;
			return value.Length != 0 && value.All(character => character is >= '0' and <= '9')
				&& ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id != 0;
		}

		private static bool IsDiscordHost(string host) => host is "discord.com" or "ptb.discord.com" or "canary.discord.com"
			or "discordapp.com" or "ptb.discordapp.com" or "canary.discordapp.com";
	}
}
