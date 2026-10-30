using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Discord.Interactions.Publishing;

namespace Orabot.Tests.Interactions;

/// <summary>Checks the actual SDK command schema offline, including compatibility, scope and optional documentation inputs.</summary>
public sealed class ApplicationCommandSchemaTests
{
	[Fact]
	public async Task Catalog_builds_all_ten_existing_commands_without_activating_runtime_services()
	{
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		// Only inert lifetime and SDK metadata services are present; no tokens, options or feature services are registered.
		using var services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddInteractionFrameworkMetadata(client.Rest).BuildServiceProvider();
		var framework = services.GetRequiredService<InteractionService>();
		var catalog = services.GetRequiredService<ApplicationCommandCatalog>();
		await catalog.InitializeAsync(services, CancellationToken.None);
		await catalog.InitializeAsync(services, CancellationToken.None);

		var definitions = catalog.GetDefinitions().Cast<SlashCommandProperties>().ToDictionary(command => command.Name.Value);

		Assert.Equal(Descriptions.Keys.OrderBy(name => name, StringComparer.Ordinal),
			definitions.Keys.OrderBy(name => name, StringComparer.Ordinal));
		Assert.Equal(10, framework.SlashCommands.Count);
		foreach (var (name, command) in definitions)
		{
			Assert.Equal(Descriptions[name], command.Description.Value);
			Assert.Equal(new[] { ApplicationIntegrationType.GuildInstall }, command.IntegrationTypes.Value);
			Assert.Equal(new[] { InteractionContextType.Guild, InteractionContextType.BotDm }, command.ContextTypes.Value.OrderBy(value => value));
			Assert.False(command.IsNsfw.IsSpecified && command.IsNsfw.Value);
			var options = command.Options.IsSpecified ? command.Options.Value : new List<ApplicationCommandOptionProperties>();
			if (!DocumentationOptionDescriptions.TryGetValue(name, out var nameDescription))
			{
				Assert.Empty(options);
				continue;
			}

			Assert.Equal(new[] { "name", "version" }, options.Select(option => option.Name));
			Assert.All(options, option =>
			{
				Assert.Equal(ApplicationCommandOptionType.String, option.Type);
				Assert.False(option.IsRequired ?? false);
			});
			Assert.Equal(nameDescription, options[0].Description);
			Assert.True(options[0].IsAutocomplete);
			Assert.Empty(options[0].Choices ?? new List<ApplicationCommandOptionChoiceProperties>());
			Assert.Equal("Which documentation version to link to? Defaults to release.", options[1].Description);
			Assert.False(options[1].IsAutocomplete);
			Assert.Equal(new[] { "release", "playtest", "development" }, options[1].Choices.Select(choice => choice.Name));
			Assert.Equal(new object[] { "release", "playtest", "development" }, options[1].Choices.Select(choice => choice.Value));

			var parameters = framework.SlashCommands.Single(info => info.Name == name).Parameters;
			Assert.Null(parameters[0].DefaultValue);
			Assert.Equal("release", parameters[1].DefaultValue);
		}
	}

	[Fact]
	public void Catalog_requires_explicit_metadata_initialization()
	{
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddInteractionFrameworkMetadata(client.Rest).BuildServiceProvider();
		var catalog = services.GetRequiredService<ApplicationCommandCatalog>();

		Assert.Throws<InvalidOperationException>(() => catalog.GetDefinitions());
	}

	static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
	{
		["supportdir"] = "Prints information about the game's support directory with default paths on each supported OS.",
		["academy"] = "Prints information about the OpenRA Academy.",
		["book"] = "Prints information about the OpenRA Book.",
		["sdk"] = "Prints information about the OpenRA ModSDK.",
		["utility"] = "Prints information about the OpenRA Utility.",
		["orabot"] = "Prints information about Orabot.",
		["lua"] = "Provides a link to the OpenRA Lua API documentation page.",
		["traits"] = "Provides a link to the OpenRA Traits documentation page.",
		["weapons"] = "Provides a link to the OpenRA Weapons documentation page.",
		["sprite-sequences"] = "Provides a link to the OpenRA Sprite Sequences documentation page."
	};

	static readonly IReadOnlyDictionary<string, string> DocumentationOptionDescriptions = new Dictionary<string, string>
	{
		["lua"] = "The name of the Lua table to link to.",
		["traits"] = "The name of the trait to link to.",
		["weapons"] = "The name of the weapon (Warhead or Projectile) to link to.",
		["sprite-sequences"] = "The name of the sprite sequence type to link to."
	};
}
