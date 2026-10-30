using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.General
{
	/// <summary>Provides the existing OpenRA information responses through declarative slash commands.</summary>
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
	public sealed class OpenRaInformationModule : InteractionModuleBase<IInteractionContext>
	{
		private readonly CancellationToken _stoppingToken;

		public OpenRaInformationModule(IHostApplicationLifetime lifetime) => _stoppingToken = lifetime.ApplicationStopping;

		private Task<IUserMessage> CompleteAsync(string text = null, Embed embed = null) =>
			Context.Interaction.ModifyOriginalResponseAsync(properties =>
			{
				if (text != null) properties.Content = text;
				if (embed != null) properties.Embeds = new[] { embed };
				properties.AllowedMentions = AllowedMentions.None;
			}, new RequestOptions { CancelToken = _stoppingToken });

		[SlashCommand("supportdir", "Prints information about the game's support directory with default paths on each supported OS.")]
		public async Task SupportDir()
		{
			await CompleteAsync("```" +
							 "The support directory is where the game keeps the saved settings, maps, replays, logs and mod assets.  The default location is as follows, but one can choose to move it to an arbitrary location by passing an Engine.SupportDir argument to the Game.exe\n\n" +
							 "Windows:      %APPDATA%\\OpenRA\\\n" +
							 "macOS:        ~/Library/Application Support/OpenRA/\n" +
							 "GNU/Linux:    $XDG_CONFIG_HOME/openra/openra\n\n" +
							 "Older releases (before playtest-20190825) used different locations, which newer versions may continue to use in some circumstances:\n\n" +
							 "Windows:      %USERPROFILE%\\Documents\\OpenRA\\\n" +
							 "GNU/Linux:    ~/.openra/\n" +
							 "```");
		}

		[SlashCommand("academy", "Prints information about the OpenRA Academy.")]
		public async Task Academy()
		{
			const string discordServerInvite = "https://discord.gg/C2CadJT";
			const string permalink = "http://academy.openra.net/";

			var embedBuilder = new EmbedBuilder
			{
				Description = "The OpenRA Academy is a separate Discord server aimed at helping players get better at the game.\n" +
							  $"Here is a [Discord invite]({discordServerInvite}), or you could use the permalink {permalink}."
			};

			await CompleteAsync(embed: embedBuilder.Build());
		}

		[SlashCommand("book", "Prints information about the OpenRA Book.")]
		public async Task Book()
		{
			const string bookUrl = "https://www.openra.net/book/";
			const string bookRepoUrl = "https://github.com/OpenRA/book";

			var embedBuilder = new EmbedBuilder
			{
				Description = $"[The OpenRA book]({bookUrl}) aims to teach new players how to play and existing players how to get better.\n" +
							  "It also covers creating new games on the OpenRA engine and how the engine works under-the-hood.\n" +
							  $"You can find the official repository for the book [on GitHub]({bookRepoUrl})."
			};

			await CompleteAsync(embed: embedBuilder.Build());
		}

		[SlashCommand("sdk", "Prints information about the OpenRA ModSDK.")]
		public async Task Sdk()
		{
			const string sdkRepoUrl = "https://github.com/OpenRA/OpenRAModSDK";

			var embedBuilder = new EmbedBuilder
			{
				Description = $"[The OpenRA ModSDK]({sdkRepoUrl}) helps you build your own games using the OpenRA engine."
			};

			await CompleteAsync(embed: embedBuilder.Build());
		}

		[SlashCommand("utility", "Prints information about the OpenRA Utility.")]
		public async Task Utility()
		{
			const string utilityWikiUrl = "https://github.com/OpenRA/OpenRA/wiki/Utility";

			var embedBuilder = new EmbedBuilder
			{
				Description = $"The OpenRA [Utility]({utilityWikiUrl}) is an executable that ships with the engine.\n" +
							  $"It is run through the command line to perform a wide range of operations like manipulating game assets " +
							  $"and checking mod YAML files for errors."
			};

			await CompleteAsync(embed: embedBuilder.Build());
		}

		[SlashCommand("orabot", "Prints information about Orabot.")]
		public async Task Orabot()
		{
			const string orabotRepoUrl = "https://github.com/OpenRA/Orabot";

			var embedBuilder = new EmbedBuilder
			{
				Description = $"[Orabot]({orabotRepoUrl}) is a modern successor to the famous OpenRA companion - the IRC orabot."
			};

			await CompleteAsync(embed: embedBuilder.Build());
		}
	}
}
