using System.Text.Json;
using Discord;
using Orabot.Bot.Discord.Interactions.Publishing;
using Orabot.Hosts.ConsoleHost.CommandDeployment;

namespace Orabot.Tests.CommandDeployment;

/// <summary>Checks deployment-mode boundaries, offline export and the CLI's read-versus-write decisions.</summary>
public sealed class CommandDeploymentTests
{
	[Theory]
	[InlineData("commands plan")]
	[InlineData("commands apply")]
	[InlineData("commands apply --global --guild 123")]
	[InlineData("commands plan --guild 0")]
	[InlineData("commands plan --guild synthetic-private-value")]
	[InlineData("commands export --prune")]
	[InlineData("commands plan --global --expected-plan bad")]
	[InlineData("commands apply --global --BotToken synthetic-private-value")]
	public void Rejects_ambiguous_or_invalid_deployment_arguments_without_echoing_values(string command)
	{
		Assert.False(CommandDeploymentArguments.TryParse(command.Split(' '), out _, out var error));
		Assert.NotEmpty(error);
		Assert.DoesNotContain("synthetic-private-value", error);
	}

	[Fact]
	public void Plan_and_apply_require_explicit_scope_and_pruning_is_opt_in()
	{
		Assert.True(CommandDeploymentArguments.TryParse(new[] { "commands", "plan", "--guild", "123" }, out var request, out _));
		Assert.Equal((ulong)123, request.Scope.GuildId);
		Assert.False(request.Prune);
		Assert.Equal(CommandDeploymentOperation.Plan, request.Operation);
	}

	[Theory]
	[InlineData("DeploymentFixture", "DeploymentFixture")]
	[InlineData(null, "Production")]
	public void Isolated_command_host_preserves_the_selected_environment(string? selected, string expected)
	{
		var builder = CommandDeploymentRunner.CreateMetadataBuilder(selected!);
		Assert.Equal(expected, builder.Environment.EnvironmentName);
		using var host = builder.Build();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Export_builds_the_real_ten_command_catalog_without_configuration_or_authentication(bool guild)
	{
		var args = guild ? new[] { "commands", "export", "--guild", "123" } : new[] { "commands", "export" };
		Assert.True(CommandDeploymentArguments.TryParse(args, out var request, out _));
		using var output = new StringWriter();
		using var errors = new StringWriter();
		Assert.Equal(0, await CommandDeploymentRunner.RunAsync(request, output, errors, CancellationToken.None));
		Assert.Equal(string.Empty, errors.ToString());
		using var schema = JsonDocument.Parse(output.ToString());
		Assert.Equal(guild ? "guild" : "global", schema.RootElement.GetProperty("scope").GetString());
		var commands = schema.RootElement.GetProperty("commands").EnumerateArray().ToArray();
		Assert.Equal(10, commands.Length);
		Assert.Contains(commands, command => command.GetProperty("name").GetString() == "traits");
		Assert.Equal(!guild, commands[0].TryGetProperty("contexts", out _));
		Assert.Equal(!guild, commands[0].TryGetProperty("integration_types", out _));
	}

	[Fact]
	public async Task Plan_previews_pruning_without_writes()
	{
		var store = new RecordingStore(new RemoteApplicationCommand(1, Command("extra")));
		var publisher = new ApplicationCommandPublisher(store);
		var request = new CommandDeploymentArguments(CommandDeploymentOperation.Plan, ApplicationCommandScope.ForGuild(123), true, null);
		using var output = new StringWriter();
		using var errors = new StringWriter();
		Assert.Equal(0, await CommandDeploymentRunner.PlanOrApplyAsync(request, publisher,
			new[] { Command("sample") }, output, errors, CancellationToken.None));
		Assert.Equal(0, store.Writes);
		Assert.Contains("Delete", output.ToString());
		Assert.Contains("Create", output.ToString());
	}

	[Fact]
	public async Task Apply_refuses_a_changed_expected_plan_without_writes()
	{
		var store = new RecordingStore();
		var request = new CommandDeploymentArguments(CommandDeploymentOperation.Apply, ApplicationCommandScope.Global, false, new string('0', 64));
		using var output = new StringWriter();
		using var errors = new StringWriter();
		Assert.Equal(2, await CommandDeploymentRunner.PlanOrApplyAsync(request, new ApplicationCommandPublisher(store),
			new[] { Command("sample") }, output, errors, CancellationToken.None));
		Assert.Equal(0, store.Writes);
		Assert.Contains("fingerprint changed", errors.ToString());
	}

	[Fact]
	public async Task Apply_uses_the_reviewed_current_scope_and_reports_success()
	{
		var store = new RecordingStore();
		var publisher = new ApplicationCommandPublisher(store);
		var definitions = new[] { Command("sample") };
		var plan = await publisher.PlanAsync(definitions, ApplicationCommandScope.ForGuild(123));
		var request = new CommandDeploymentArguments(CommandDeploymentOperation.Apply, plan.Scope, false, plan.Fingerprint);
		using var output = new StringWriter();
		using var errors = new StringWriter();
		Assert.Equal(0, await CommandDeploymentRunner.PlanOrApplyAsync(request, publisher, definitions, output, errors, CancellationToken.None));
		Assert.Equal(1, store.Writes);
		Assert.Equal((ulong)123, store.LastScope!.GuildId);
		Assert.Contains("Applied", errors.ToString());
	}

	static SlashCommandProperties Command(string name) => new SlashCommandBuilder
	{
		Name = name, Description = "A test command.", IsNsfw = false,
		ContextTypes = new() { InteractionContextType.Guild, InteractionContextType.BotDm },
		IntegrationTypes = new() { ApplicationIntegrationType.GuildInstall }
	}.Build();

	sealed class RecordingStore : IApplicationCommandStore
	{
		readonly List<RemoteApplicationCommand> remote;
		public RecordingStore(params RemoteApplicationCommand[] initial) => remote = initial.ToList();
		public int Writes { get; private set; }
		public ApplicationCommandScope? LastScope { get; private set; }
		public Task<IReadOnlyList<RemoteApplicationCommand>> ListAsync(ApplicationCommandScope scope, CancellationToken cancellationToken)
		{
			LastScope = scope;
			return Task.FromResult<IReadOnlyList<RemoteApplicationCommand>>(remote.ToArray());
		}
		public Task UpsertAsync(ApplicationCommandScope scope, ApplicationCommandProperties definition, CancellationToken cancellationToken)
		{
			Writes++; LastScope = scope;
			remote.Add(new RemoteApplicationCommand((ulong)100 + (ulong)remote.Count, definition));
			return Task.CompletedTask;
		}
		public Task DeleteAsync(ApplicationCommandScope scope, ulong commandId, CancellationToken cancellationToken)
		{
			Writes++; LastScope = scope;
			remote.RemoveAll(command => command.Id == commandId);
			return Task.CompletedTask;
		}
	}
}
