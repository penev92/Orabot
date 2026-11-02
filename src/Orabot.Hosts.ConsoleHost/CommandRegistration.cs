using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.Documentation;

namespace Orabot.Hosts.ConsoleHost
{
	/// <summary>Explicitly registers the bot's modules using Discord.Net without starting the Gateway or workers.</summary>
	internal static class CommandRegistration
	{
		internal const string Usage = """
			Orabot
			  No subcommand: run the console bot (Ctrl+C to stop).
			  commands register --guild <id>
			  commands register --global

			Registration is explicit and uses Discord.Net with deleteMissing: false.
			""";

		internal static bool TryParse(string[] args, out ulong? guildId, out string error)
		{
			guildId = null;
			error = "Choose commands register --guild <id> or commands register --global.";
			if (args.Length < 3 || !string.Equals(args[0], "commands", StringComparison.OrdinalIgnoreCase)
				|| !string.Equals(args[1], "register", StringComparison.OrdinalIgnoreCase))
				return false;
			if (args.Length == 3 && args[2] == "--global")
			{
				error = null;
				return true;
			}
			if (args.Length != 4 || args[2] != "--guild"
				|| !ulong.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
				return false;
			guildId = id;
			error = null;
			return true;
		}

		internal static async Task<int> RunAsync(ulong? guildId, TextWriter output, TextWriter error, CancellationToken cancellationToken)
		{
			var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });
			OrabotConfiguration.Configure(builder, Array.Empty<string>());
			builder.Logging.ClearProviders();
			var token = builder.Configuration["BotToken"];
			if (string.IsNullOrWhiteSpace(token))
			{
				await error.WriteLineAsync("BotToken must be configured for command registration.");
				return 2;
			}
			using var client = new DiscordRestClient();
			builder.Services.AddInteractionFramework(client).AddDocumentationFeature(builder.Configuration)
				.AddSingleton(TimeProvider.System);
			using var host = builder.Build();
			OrabotConfiguration.Validate(host.Services);
			var interactions = host.Services.GetRequiredService<InteractionService>();
			await interactions.AddModulesAsync(typeof(global::Orabot.Bot.Discord.Bot).Assembly, host.Services);
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				await client.LoginAsync(TokenType.Bot, token);
				cancellationToken.ThrowIfCancellationRequested();
				// The SDK owns command projection and registration. These APIs have no cancellation
				// parameter: await an in-flight registration before disposing its client.
				if (guildId.HasValue)
					await interactions.RegisterCommandsToGuildAsync(guildId.Value, deleteMissing: false);
				else
					await interactions.RegisterCommandsGloballyAsync(deleteMissing: false);
				cancellationToken.ThrowIfCancellationRequested();
				await output.WriteLineAsync(guildId.HasValue ? $"Registered commands in guild {guildId.Value}." : "Registered global commands.");
				return 0;
			}
			finally
			{
				try { await client.LogoutAsync(); }
				catch (Exception exception)
				{
					await error.WriteLineAsync($"Command client cleanup failed ({exception.GetType().Name}).");
				}
			}
		}
	}
}
