using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.Roles;
using Orabot.Tests.Interactions;

namespace Orabot.Tests.Roles;

public sealed class RoleManagementModuleTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Real_sdk_role_command_lists_or_toggles_without_requiring_member_manage_roles(bool selectRole)
	{
		var fixture = new RolesFixture();
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddSingleton<IHostApplicationLifetime>(lifetime)
			.AddSingleton(fixture.Service()).AddInteractionFramework(client.Rest).BuildServiceProvider();
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<RoleManagementModule>(services);
		var command = Assert.Single(interactions.SlashCommands);
		Assert.Equal("role", command.Name);
		Assert.Equal(InteractionContextType.Guild, Assert.Single(command.ContextTypes));
		var option = Assert.Single(command.Parameters);
		Assert.False(option.IsRequired);
		Assert.Equal(ApplicationCommandOptionType.Role, option.DiscordOptionType);
		// The Socket SDK resolves the wire snowflake before exposing the option to
		// InteractionService, so this interface-level fixture supplies the resolved role.
		var interaction = selectRole
			? new OfflineSlashCommandInteraction("role", new OfflineCommandOption("role", fixture.Target, ApplicationCommandOptionType.Role))
			: new OfflineSlashCommandInteraction("role");
		interaction.User = fixture.Member;
		interaction.GuildId = RolesFixture.GuildId;
		await interaction.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, interaction, fixture.Guild, fixture.Channel), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		var response = Assert.Single(interaction.ResponseEdits);
		Assert.Equal(AllowedMentions.None.AllowedTypes, response.AllowedMentions.Value.AllowedTypes);
		Assert.Equal(new[] { "defer:public", "modify" }, interaction.Calls);
		if (selectRole) Assert.Equal(new[] { "add:30" }, fixture.Writes);
		else { Assert.Contains("Players", response.Content.Value); Assert.Empty(fixture.Writes); }
	}
}
