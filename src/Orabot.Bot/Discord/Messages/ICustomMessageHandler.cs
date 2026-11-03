using Discord;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Discord.Messages
{
	public interface ICustomMessageHandler
	{
		bool CanHandle(IUserMessage message);
		Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default);
	}
}
