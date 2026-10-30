using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Discord.Interactions.Publishing;

namespace Orabot.Hosts.ConsoleHost.CommandDeployment
{
	/// <summary>Runs command tooling without starting the Gateway, refresh workers or ordinary bot service graph.</summary>
	internal static class CommandDeploymentRunner
	{
		internal const string Usage = """
			Orabot
			  No subcommand: run the console bot (Ctrl+C to stop).
			  commands export [--global | --guild <id>]
			  commands plan (--global | --guild <id>) [--prune]
			  commands apply (--global | --guild <id>) [--prune] [--expected-plan <fingerprint>]

			Export is offline and needs no configuration or token. Plan reads Discord; only apply writes.
			Extra remote commands are preserved unless --prune is supplied explicitly.
			""";

		internal static async Task<int> RunAsync(CommandDeploymentArguments request, TextWriter output,
			TextWriter error, CancellationToken cancellationToken)
		{
			if (request.Operation == CommandDeploymentOperation.Help)
			{
				await output.WriteLineAsync(Usage);
				return 0;
			}
			using var client = new DiscordRestClient();
			var builder = CreateMetadataBuilder(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));
			if (request.Operation != CommandDeploymentOperation.Export)
				OrabotConfiguration.Configure(builder, Array.Empty<string>());
			builder.Services.AddInteractionFrameworkMetadata(client);
			using var host = builder.Build();
			var catalog = host.Services.GetRequiredService<ApplicationCommandCatalog>();
			await catalog.InitializeAsync(host.Services, cancellationToken);
			var definitions = catalog.GetDefinitions();
			if (request.Operation == CommandDeploymentOperation.Export)
			{
				await output.WriteLineAsync(ApplicationCommandPublisher.ExportJson(definitions, request.Scope));
				return 0;
			}

			var token = builder.Configuration["BotToken"];
			if (string.IsNullOrWhiteSpace(token))
			{
				await error.WriteLineAsync("BotToken must be configured for command planning or applying.");
				return 2;
			}
			try
			{
				// Authentication is deliberately outside the publisher's safe exception boundary.
				// The Program entry point reports SDK authentication failures by type only.
				await client.LoginAsync(TokenType.Bot, token);
				cancellationToken.ThrowIfCancellationRequested();
				var publisher = new ApplicationCommandPublisher(new DiscordApplicationCommandStore(client));
				return await PlanOrApplyAsync(request, publisher, definitions, output, error, cancellationToken);
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

		// DisableDefaults keeps export free of configuration providers, so select the same
		// DOTNET_ENVIRONMENT explicitly before plan/apply add ordinary configuration.
		internal static HostApplicationBuilder CreateMetadataBuilder(string environmentName) =>
			Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
			{
				DisableDefaults = true,
				ContentRootPath = AppContext.BaseDirectory,
				EnvironmentName = environmentName
			});

		internal static async Task<int> PlanOrApplyAsync(CommandDeploymentArguments request,
			ApplicationCommandPublisher publisher, System.Collections.Generic.IReadOnlyList<ApplicationCommandProperties> definitions,
			TextWriter output, TextWriter error, CancellationToken cancellationToken)
		{
			try
			{
				var plan = await publisher.PlanAsync(definitions, request.Scope, request.Prune, cancellationToken);
				await output.WriteLineAsync(SerializePlan(plan));
				if (request.Operation == CommandDeploymentOperation.Plan)
					return 0;
				if (request.ExpectedPlan != null && !string.Equals(request.ExpectedPlan, plan.Fingerprint, StringComparison.Ordinal))
				{
					await error.WriteLineAsync("The plan fingerprint changed. Review the new plan before applying.");
					return 2;
				}
				await publisher.ApplyAsync(plan, cancellationToken);
				await error.WriteLineAsync(plan.HasChanges ? "Applied the command plan." : "Commands already match the catalog.");
				return 0;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await error.WriteLineAsync("Command operation cancelled. Generate a new plan before retrying an apply.");
				return 130;
			}
			catch (InvalidOperationException exception)
			{
				// Publisher exceptions contain only its own safe deployment diagnostics, never raw transport values.
				await error.WriteLineAsync(exception.Message);
				return 1;
			}
		}

		private static string SerializePlan(ApplicationCommandPlan plan) => JsonSerializer.Serialize(new
		{
			scope = plan.Scope.IsGlobal ? "global" : "guild",
			guildId = plan.Scope.GuildId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
			prune = plan.Prune,
			fingerprint = plan.Fingerprint,
			hasChanges = plan.HasChanges,
			changes = plan.Changes.Select(change => new
			{
				kind = change.Kind.ToString(), type = change.Type.ToString(), name = change.Name,
				existingId = change.ExistingId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
				before = change.BeforeJson == null ? null : JsonNode.Parse(change.BeforeJson),
				after = change.AfterJson == null ? null : JsonNode.Parse(change.AfterJson)
			})
		}, new JsonSerializerOptions { WriteIndented = true });
	}
}
