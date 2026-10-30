using System;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Orabot.Bot.Discord.Interactions.Publishing;

namespace Orabot.Bot.DependencyInjection
{
	/// <summary>Shares SDK configuration and metadata discovery between the running bot and offline command tooling.</summary>
	public static class InteractionFrameworkRegistration
	{
		/// <summary>Registers metadata services for a caller-owned REST or socket client without logging in.</summary>
		public static IServiceCollection AddInteractionFrameworkMetadata(this IServiceCollection services, DiscordRestClient client) =>
			services.AddInteractionFrameworkMetadata(_ => client);

		/// <summary>Registers an awaited framework and invocation-scoped deferred dependencies.</summary>
		public static IServiceCollection AddInteractionFrameworkMetadata(this IServiceCollection services,
			Func<IServiceProvider, DiscordRestClient> clientFactory)
		{
			services.AddSingleton(provider => new InteractionService(clientFactory(provider), new InteractionServiceConfig
			{
				// The host-owned queue provides concurrency; SDK execution stays awaited until its scope is finished.
				DefaultRunMode = RunMode.Sync,
				AutoServiceScopes = true,
				EnableAutocompleteHandlers = true
			}));
			services.AddSingleton<ApplicationCommandCatalog>();
			services.AddTransient(typeof(Lazy<>), typeof(DeferredDependency<>));
			return services;
		}

		// Discord.Net constructs prototype modules during metadata discovery. Lazy feature
		// dependencies keep discovery independent of runtime configuration and I/O, while a
		// real invocation resolves from the scope created by AutoServiceScopes.
		private sealed class DeferredDependency<T> : Lazy<T> where T : class
		{
			public DeferredDependency(IServiceProvider services) : base(services.GetRequiredService<T>) { }
		}
	}
}
