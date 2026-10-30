namespace Orabot.Bot.Discord
{
	public sealed class DiscordBotOptions
	{
		public string BotToken { get; set; }
		public int MessageCacheSize { get; set; } = 100;
	}
}
