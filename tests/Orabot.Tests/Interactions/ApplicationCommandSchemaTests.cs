using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.General;
using Orabot.Tests.Documentation;

namespace Orabot.Tests.Interactions;

/// <summary>Checks the ten retained command definitions directly in Discord.Net's metadata model.</summary>
public sealed class ApplicationCommandSchemaTests
{
	[Fact]
	public async Task Modules_preserve_command_names_contexts_and_optional_documentation_inputs()
	{
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(DocumentationOptionsTests.ValidSettings()).Build();
		using var services = new ServiceCollection().AddLogging().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddSingleton(TimeProvider.System).AddDocumentationFeature(configuration)
			.AddInteractionFramework(client.Rest).BuildServiceProvider();
		var framework = services.GetRequiredService<InteractionService>();
		await framework.AddModulesAsync(typeof(OpenRaInformationModule).Assembly, services);
		var commands = framework.SlashCommands.ToDictionary(command => command.Name);

		Assert.Equal(Descriptions.Keys.OrderBy(name => name, StringComparer.Ordinal), commands.Keys.OrderBy(name => name, StringComparer.Ordinal));
		Assert.Equal(4, framework.Modules.SelectMany(module => module.AutocompleteCommands).Count());
		foreach (var (name, command) in commands)
		{
			Assert.Equal(Descriptions[name], command.Description);
			Assert.Equal(new[] { ApplicationIntegrationType.GuildInstall }, command.IntegrationTypes);
			Assert.Equal(new[] { InteractionContextType.Guild, InteractionContextType.BotDm }, command.ContextTypes.OrderBy(value => value));
			Assert.Equal(RunMode.Sync, command.RunMode);
			Assert.False(command.IsNsfw);
			var options = command.Parameters;
			if (!DocumentationOptionDescriptions.TryGetValue(name, out var nameDescription))
			{
				Assert.Empty(options);
				continue;
			}

			Assert.Equal(new[] { "name", "version" }, options.Select(option => option.Name));
			Assert.All(options, option =>
			{
				Assert.Equal(ApplicationCommandOptionType.String, option.DiscordOptionType);
				Assert.False(option.IsRequired);
			});
			Assert.Equal(nameDescription, options[0].Description);
			Assert.True(options[0].IsAutocomplete);
			Assert.Empty(options[0].Choices);
			Assert.Equal("Which documentation version to link to? Defaults to release.", options[1].Description);
			Assert.False(options[1].IsAutocomplete);
			Assert.Equal(new[] { "release", "playtest", "development" }, options[1].Choices.Select(choice => choice.Name));
			Assert.Equal(new object[] { "release", "playtest", "development" }, options[1].Choices.Select(choice => choice.Value));
			Assert.Null(options[0].DefaultValue);
			Assert.Equal("release", options[1].DefaultValue);
		}
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
