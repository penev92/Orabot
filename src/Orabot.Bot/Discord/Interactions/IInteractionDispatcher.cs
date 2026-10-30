using System;
using System.Threading;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Orabot.Bot.Discord.Interactions
{
	/// <summary>Initializes interaction modules and admits Gateway interactions without running external work on the Gateway callback.</summary>
	public interface IInteractionDispatcher
	{
		Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken);
		Task HandleInteractionAsync(SocketInteraction interaction);
	}
}
