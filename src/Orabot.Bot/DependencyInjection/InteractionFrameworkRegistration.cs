using System;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;

namespace Orabot.Bot.DependencyInjection
{
	/// <summary>Configures Discord.Net execution for the running bot and explicit command registration.</summary>
	public static class InteractionFrameworkRegistration
	{
		/// <summary>Registers the framework against a caller-owned REST client.</summary>
		public static IServiceCollection AddInteractionFramework(this IServiceCollection services, DiscordRestClient client) =>
			services.AddInteractionFramework(_ => client);

		/// <summary>Uses awaited SDK execution and automatic invocation scopes inside the hosted work queue.</summary>
		public static IServiceCollection AddInteractionFramework(this IServiceCollection services,
			Func<IServiceProvider, DiscordRestClient> clientFactory)
		{
			services.AddSingleton(provider => new InteractionService(clientFactory(provider), new InteractionServiceConfig
			{
				DefaultRunMode = RunMode.Sync,
				AutoServiceScopes = true,
				// Explicit autocomplete callbacks own their bounded response.
				EnableAutocompleteHandlers = false
			}));
			return services;
		}
	}
}
