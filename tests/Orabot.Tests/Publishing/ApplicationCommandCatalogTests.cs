using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Discord.Interactions.Publishing;
using Xunit;

namespace Orabot.Tests.Publishing
{
	/// <summary>Verifies offline SDK metadata discovery, independent schema copies and attributed command projection.</summary>
	public sealed class ApplicationCommandCatalogTests
	{
		[Fact]
		public async Task MetadataInitializesOnceOfflineAndReturnedDefinitionsAreIndependent()
		{
			using var client = new DiscordRestClient();
			var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
			builder.Services.AddInteractionFrameworkMetadata(client);
			using var host = builder.Build();
			var services = host.Services;
			var interactions = services.GetRequiredService<InteractionService>();
			var catalog = services.GetRequiredService<ApplicationCommandCatalog>();
			Assert.Throws<InvalidOperationException>(() => catalog.GetDefinitions());
			await Task.WhenAll(catalog.InitializeAsync(services), catalog.InitializeAsync(services));
			var moduleCount = interactions.Modules.Count;
			var before = ApplicationCommandPublisher.ExportJson(catalog.GetDefinitions(), ApplicationCommandScope.Global);
			await catalog.InitializeAsync(services);
			Assert.Equal(moduleCount, interactions.Modules.Count);
			Assert.Equal(before, ApplicationCommandPublisher.ExportJson(catalog.GetDefinitions(), ApplicationCommandScope.Global));
			Assert.NotEmpty(catalog.GetDefinitions());
			catalog.GetDefinitions().First().Name = "external-mutation";
			Assert.Equal(before, ApplicationCommandPublisher.ExportJson(catalog.GetDefinitions(), ApplicationCommandScope.Global));
		}

		[Fact]
		public async Task CancellationBeforeInitializationDoesNotLoadModules()
		{
			using var client = new DiscordRestClient();
			var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
			builder.Services.AddInteractionFrameworkMetadata(client);
			using var host = builder.Build();
			var services = host.Services;
			var interactions = services.GetRequiredService<InteractionService>();
			var catalog = services.GetRequiredService<ApplicationCommandCatalog>();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => catalog.InitializeAsync(services, new CancellationToken(true)));
			Assert.Empty(interactions.Modules);
		}

		[Fact]
		public async Task PublicMetadataProjectionPreservesGroupsSubgroupsChoicesAndContextCommands()
		{
			using var client = new DiscordRestClient();
			using var interactions = new InteractionService(client, new InteractionServiceConfig { DefaultRunMode = RunMode.Sync, AutoServiceScopes = true });
			using var services = new ServiceCollection().BuildServiceProvider();
			await interactions.AddModuleAsync<PublishingProjectionGroup>(services);
			var definitions = CommandDefinitionProjection.Project(interactions);
			var grouped = Assert.Single(definitions.OfType<SlashCommandProperties>());
			Assert.Equal("root", grouped.Name.Value);
			var direct = grouped.Options.Value.Single(x => x.Type == ApplicationCommandOptionType.SubCommand);
			Assert.Equal("direct", direct.Name);
			var choice = Assert.Single(Assert.Single(direct.Options).Choices);
			Assert.Equal("one", choice.Value);
			var subgroup = grouped.Options.Value.Single(x => x.Type == ApplicationCommandOptionType.SubCommandGroup);
			Assert.Equal("child", subgroup.Name);
			Assert.Equal("nested", Assert.Single(subgroup.Options).Name);
			Assert.Equal(ApplicationCommandOptionType.Boolean, Assert.Single(Assert.Single(subgroup.Options).Options).Type);
			Assert.Equal("Inspect", Assert.Single(definitions.OfType<MessageCommandProperties>()).Name.Value);
		}
	}

	[Group("root", "Root group.")]
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
	public class PublishingProjectionGroup : InteractionModuleBase<IInteractionContext>
	{
		[SlashCommand("direct", "A direct subcommand.")]
		public Task Direct([Choice("One", "one")] string value = "one") => Task.CompletedTask;

		[MessageCommand("Inspect")]
		public Task Inspect(IMessage message) => Task.CompletedTask;

		[Group("child", "Child group.")]
		public class Child : InteractionModuleBase<IInteractionContext>
		{
			[SlashCommand("nested", "A nested subcommand.")]
			public Task Nested(bool enabled = false) => Task.CompletedTask;
		}
	}
}
