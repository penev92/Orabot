using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace Orabot.Bot.Discord
{
	// This narrow seam tests host lifetime without a real Discord login. Event handling,
	// message operations and Discord state continue to use the SDK directly.
	internal interface IDiscordConnectionLifecycle
	{
		Task LoginAsync(string botToken);
		Task StartAsync();
		Task StopAsync();
		Task LogoutAsync();
	}

	internal sealed class DiscordConnectionLifecycle : IDiscordConnectionLifecycle
	{
		private readonly DiscordSocketClient _client;

		public DiscordConnectionLifecycle(DiscordSocketClient client) => _client = client;

		public Task LoginAsync(string botToken) => _client.LoginAsync(TokenType.Bot, botToken);
		public Task StartAsync() => _client.StartAsync();
		public Task StopAsync() => _client.StopAsync();
		public Task LogoutAsync() => _client.LogoutAsync();
	}
}
