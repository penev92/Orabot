using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.Features.General;
using Orabot.Bot.DependencyInjection;

namespace Orabot.Tests.Interactions;

/// <summary>Executes the six information commands through Discord.Net and preserves their existing public responses.</summary>
public sealed class OpenRaInformationModuleTests
{
	[Theory]
	[InlineData("supportdir")]
	[InlineData("academy")]
	[InlineData("book")]
	[InlineData("sdk")]
	[InlineData("utility")]
	[InlineData("orabot")]
	public async Task Real_framework_execution_edits_the_public_deferred_response_once(string command)
	{
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddInteractionFramework(client.Rest).BuildServiceProvider();
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<OpenRaInformationModule>(services);
		var interaction = new OfflineSlashCommandInteraction(command);
		await interaction.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, interaction), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.Equal(1, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal(1, interaction.ModifyCount);
		Assert.Equal(new[] { "defer:public", "modify" }, interaction.Calls);
		Assert.Empty(interaction.Followups);
		var response = Assert.Single(interaction.ResponseEdits);
		if (command == "supportdir")
		{
			Assert.Equal(SupportDirectoryText, response.Content.Value);
			Assert.False(response.Embeds.IsSpecified);
		}
		else
		{
			Assert.False(response.Content.IsSpecified);
			var embed = Assert.Single(response.Embeds.Value);
			Assert.Equal(EmbedDescriptions[command], embed.Description);
			Assert.Null(embed.Title);
			Assert.Empty(embed.Fields);
		}
	}

	[Fact]
	public async Task Host_shutdown_cancels_the_original_response_write()
	{
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddInteractionFramework(client.Rest).BuildServiceProvider();
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<OpenRaInformationModule>(services);
		var interaction = new OfflineSlashCommandInteraction("orabot");
		await interaction.DeferAsync();
		lifetime.StopApplication();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, interaction), services);

		Assert.False(result.IsSuccess);
		Assert.Empty(interaction.ResponseEdits);
		Assert.Equal(0, interaction.ModifyCount);
	}

	const string SupportDirectoryText = "```" +
		"The support directory is where the game keeps the saved settings, maps, replays, logs and mod assets.  The default location is as follows, but one can choose to move it to an arbitrary location by passing an Engine.SupportDir argument to the Game.exe\n\n" +
		"Windows:      %APPDATA%\\OpenRA\\\n" +
		"macOS:        ~/Library/Application Support/OpenRA/\n" +
		"GNU/Linux:    $XDG_CONFIG_HOME/openra/openra\n\n" +
		"Older releases (before playtest-20190825) used different locations, which newer versions may continue to use in some circumstances:\n\n" +
		"Windows:      %USERPROFILE%\\Documents\\OpenRA\\\n" +
		"GNU/Linux:    ~/.openra/\n" +
		"```";

	static readonly IReadOnlyDictionary<string, string> EmbedDescriptions = new Dictionary<string, string>
	{
		["academy"] = "The OpenRA Academy is a separate Discord server aimed at helping players get better at the game.\n" +
			"Here is a [Discord invite](https://discord.gg/C2CadJT), or you could use the permalink http://academy.openra.net/.",
		["book"] = "[The OpenRA book](https://www.openra.net/book/) aims to teach new players how to play and existing players how to get better.\n" +
			"It also covers creating new games on the OpenRA engine and how the engine works under-the-hood.\n" +
			"You can find the official repository for the book [on GitHub](https://github.com/OpenRA/book).",
		["sdk"] = "[The OpenRA ModSDK](https://github.com/OpenRA/OpenRAModSDK) helps you build your own games using the OpenRA engine.",
		["utility"] = "The OpenRA [Utility](https://github.com/OpenRA/OpenRA/wiki/Utility) is an executable that ships with the engine.\n" +
			"It is run through the command line to perform a wide range of operations like manipulating game assets and checking mod YAML files for errors.",
		["orabot"] = "[Orabot](https://github.com/OpenRA/Orabot) is a modern successor to the famous OpenRA companion - the IRC orabot."
	};
}
