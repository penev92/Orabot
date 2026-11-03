using Orabot.Bot.Features.Quotes;

namespace Orabot.Tests.Quotes;

/// <summary>Checks supported lossless snowflake forms and rejects unrelated or malformed URLs.</summary>
public sealed class QuoteIdentifierTests
{
	[Theory]
	[InlineData("18446744073709551615", null, null)]
	[InlineData("200-18446744073709551615", 200UL, null)]
	[InlineData("https://discord.com/channels/100/200/18446744073709551615", 200UL, 100UL)]
	[InlineData("https://canary.discord.com/channels/100/200/18446744073709551615", 200UL, 100UL)]
	[InlineData("https://discordapp.com/channels/100/200/18446744073709551615", 200UL, 100UL)]
	public void Parses_supported_identifiers_without_numeric_precision_loss(string value, ulong? channelId, ulong? guildId)
	{
		Assert.True(DiscordMessageIdentifier.TryParse(value, out var identifier));
		Assert.Equal(ulong.MaxValue, identifier.MessageId);
		Assert.Equal(channelId, identifier.ChannelId);
		Assert.Equal(guildId, identifier.GuildId);
	}

	[Theory]
	[InlineData("")]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1-2-3")]
	[InlineData("18446744073709551616")]
	[InlineData("https://example.org/channels/1/2/3")]
	[InlineData("https://discord.com.example.org/channels/1/2/3")]
	[InlineData("http://discord.com/channels/1/2/3")]
	[InlineData("https://discord.com/channels/@me/2/3")]
	[InlineData("https://user:pass@discord.com/channels/1/2/3")]
	[InlineData("https://discord.com:444/channels/1/2/3")]
	[InlineData("https://discord.com/channels/1/2/3?query=4")]
	[InlineData("https://discord.com/channels/1/2/3#fragment")]
	public void Rejects_invalid_or_external_identifiers(string value) => Assert.False(DiscordMessageIdentifier.TryParse(value, out _));
}
