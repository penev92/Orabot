using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.General;
using Orabot.Tests.Interactions;
using Orabot.Tests.TestDoubles;

namespace Orabot.Tests.General;

public sealed class GeneralModuleTests
{
	[Fact]
	public async Task Command_metadata_exposes_only_the_intended_guild_install_contexts()
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		foreach (var name in new[] { "help", "hi", "say" })
		{
			var command = fixture.Interactions.SlashCommands.Single(item => item.Name == name);
			Assert.Equal(ApplicationIntegrationType.GuildInstall, Assert.Single(command.IntegrationTypes));
			if (name == "say") Assert.Equal(InteractionContextType.Guild, Assert.Single(command.ContextTypes));
			else Assert.Equal(new[] { InteractionContextType.Guild, InteractionContextType.BotDm }, command.ContextTypes.OrderBy(value => value));
		}
	}

	[Fact]
	public async Task Help_lists_real_sdk_commands_and_message_context_actions()
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		var (result, interaction) = await fixture.ExecuteAsync("help");
		Assert.True(result.IsSuccess, result.ErrorReason);
		var content = Text(interaction);
		foreach (var name in new[] { "help", "hi", "say", "general-probe", "General inspect" })
			Assert.Contains(name, content);
		AssertResponsePolicy(interaction);
	}

	[Fact]
	public async Task Help_chunks_large_metadata_lists_without_omitting_commands()
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		var names = Enumerable.Range(0, 80).Select(index => "entry-" + index.ToString("D2") + "-" + new string('x', 20)).ToArray();
		await fixture.Interactions.CreateModuleAsync("AdditionalHelpEntries", fixture.Services, module =>
		{
			foreach (var name in names)
				module.AddSlashCommand(name, (_, _, _, _) => Task.FromResult<IResult>(ExecuteResult.FromSuccess()),
					command => command.WithDescription("An additional help entry."));
		});
		var (result, interaction) = await fixture.ExecuteAsync("help");
		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.NotEmpty(interaction.Followups);
		foreach (var name in names) Assert.Contains(name, Text(interaction));
		Assert.All(interaction.ResponseEdits.Concat(interaction.Followups), response => Assert.InRange(response.Content.Value.Length, 1, 1900));
		AssertResponsePolicy(interaction);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Hi_edits_the_public_response_in_guild_or_bot_dm(bool dm)
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		var (result, interaction) = await fixture.ExecuteAsync("hi", dm: dm);
		Assert.True(result.IsSuccess, result.ErrorReason);
		var response = Assert.Single(interaction.ResponseEdits);
		Assert.Contains("Hi", response.Content.Value);
		Assert.Contains("<@200>", response.Content.Value);
		AssertResponsePolicy(interaction);
	}

	[Theory]
	[InlineData("Trusted", false, true)]
	[InlineData("trusted", false, false)]
	[InlineData("Other", false, false)]
	[InlineData("Trusted", true, false)]
	public async Task Say_uses_case_sensitive_trusted_names_and_denies_dm(string roleName, bool dm, bool allowed)
	{
		using var fixture = new Fixture(roleName);
		await fixture.InitializeAsync();
		const string text = "@everyone <@&999> a literal message";
		var (result, interaction) = await fixture.ExecuteAsync("say", text, dm);
		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.Equal(allowed ? text : "No rights!", Assert.Single(interaction.ResponseEdits).Content.Value);
		AssertResponsePolicy(interaction);
	}

	[Fact]
	public async Task Say_declares_and_enforces_the_message_length_limit()
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		var parameter = Assert.Single(fixture.Interactions.SlashCommands.Single(command => command.Name == "say").Parameters);
		Assert.Equal(2000, parameter.MaxLength);
		var tooLong = new string('x', 2001);
		var (result, interaction) = await fixture.ExecuteAsync("say", tooLong);
		if (result.IsSuccess)
		{
			var response = Assert.Single(interaction.ResponseEdits);
			Assert.NotEqual(tooLong, response.Content.Value);
			Assert.InRange(response.Content.Value.Length, 1, 2000);
		}
		else Assert.Empty(interaction.ResponseEdits);
		Assert.Empty(interaction.Followups);
	}

	[Fact]
	public async Task Shutdown_cancels_writes_without_a_second_initial_response()
	{
		using var fixture = new Fixture();
		await fixture.InitializeAsync();
		fixture.Lifetime.StopApplication();
		var (result, interaction) = await fixture.ExecuteAsync("hi");
		Assert.False(result.IsSuccess);
		Assert.Empty(interaction.ResponseEdits);
		Assert.Empty(interaction.Followups);
		Assert.Equal(1, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
	}

	static string Text(OfflineSlashCommandInteraction interaction) => string.Join("\n",
		interaction.ResponseEdits.Concat(interaction.Followups).Select(response => response.Content.Value));

	static void AssertResponsePolicy(OfflineSlashCommandInteraction interaction)
	{
		Assert.Equal(1, interaction.DeferCount);
		Assert.Equal(0, interaction.RespondCount);
		Assert.Equal("defer:public", interaction.Calls.First());
		Assert.Single(interaction.ResponseEdits);
		Assert.All(interaction.ResponseEdits.Concat(interaction.Followups), response =>
			Assert.Equal(AllowedMentions.None.AllowedTypes, response.AllowedMentions.Value.AllowedTypes));
	}

	sealed class Fixture : IDisposable
	{
		readonly DiscordSocketClient client = new();
		readonly IGuild guild;
		readonly IGuildUser user;
		public OfflineHostApplicationLifetime Lifetime { get; } = new();
		public ServiceProvider Services { get; }
		public InteractionService Interactions => Services.GetRequiredService<InteractionService>();

		public Fixture(string roleName = "Trusted")
		{
			var role = DiscordTestProxy.Create<IRole>((method, _) => method switch
			{
				"get_Id" => (ulong)20, "get_Name" => roleName,
				_ => throw new NotSupportedException(method)
			});
			guild = DiscordTestProxy.Create<IGuild>((method, _) => method switch
			{
				"get_Id" => (ulong)100, "get_Roles" => new[] { role },
				_ => throw new NotSupportedException(method)
			});
			user = DiscordTestProxy.Create<IGuildUser>((method, _) => method switch
			{
				"get_Id" => (ulong)200, "get_GuildId" => (ulong)100,
				"get_RoleIds" => new ulong[] { 20 }, "get_Mention" => "<@200>",
				_ => throw new NotSupportedException(method)
			});
			Services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(Lifetime)
				.AddSingleton<IOptions<TrustedRoleOptions>>(Options.Create(new TrustedRoleOptions { TrustedRoles = "Trusted" }))
				.AddInteractionFramework(client.Rest).BuildServiceProvider();
		}

		public async Task InitializeAsync()
		{
			await Interactions.AddModuleAsync<GeneralModule>(Services);
			await Interactions.AddModuleAsync<GeneralHelpProbeModule>(Services);
		}

		public async Task<(IResult Result, OfflineSlashCommandInteraction Interaction)> ExecuteAsync(string command, string? text = null, bool dm = false)
		{
			var interaction = text == null ? new OfflineSlashCommandInteraction(command)
				: new OfflineSlashCommandInteraction(command, new OfflineCommandOption("message", text));
			interaction.User = user;
			interaction.GuildId = dm ? null : (ulong)100;
			interaction.ContextType = dm ? InteractionContextType.BotDm : InteractionContextType.Guild;
			await interaction.DeferAsync();
			var result = await Interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, interaction, dm ? null! : guild), Services);
			return (result, interaction);
		}

		public void Dispose() { Services.Dispose(); client.Dispose(); Lifetime.Dispose(); }
	}
}

public sealed class GeneralHelpProbeModule : InteractionModuleBase<IInteractionContext>
{
	[SlashCommand("general-probe", "A help discovery probe.")]
	public Task Probe() => Task.CompletedTask;

	[MessageCommand("General inspect")]
	public Task Inspect(IMessage message) => Task.CompletedTask;
}
