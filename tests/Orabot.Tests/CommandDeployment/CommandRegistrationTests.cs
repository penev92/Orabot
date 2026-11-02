using Orabot.Hosts.ConsoleHost;

namespace Orabot.Tests.CommandDeployment;

/// <summary>Checks that publication requires one explicit target and rejects old deployment options before authentication.</summary>
public sealed class CommandRegistrationTests
{
	[Theory]
	[InlineData("commands register")]
	[InlineData("commands register --global --guild 123")]
	[InlineData("commands register --guild 0")]
	[InlineData("commands register --guild synthetic-private-value")]
	[InlineData("commands register --guild 123 --prune")]
	[InlineData("commands register --global --BotToken synthetic-private-value")]
	[InlineData("commands export")]
	[InlineData("commands plan --global")]
	[InlineData("commands apply --global")]
	public void Invalid_targets_and_old_deployment_options_are_rejected_without_echoing_values(string command)
	{
		Assert.False(CommandRegistration.TryParse(command.Split(' '), out _, out var error));
		Assert.NotEmpty(error);
		Assert.DoesNotContain("synthetic-private-value", error);
	}

	[Fact]
	public void Explicit_global_target_has_no_guild_id()
	{
		Assert.True(CommandRegistration.TryParse(new[] { "commands", "register", "--global" }, out var guildId, out var error));
		Assert.Null(guildId);
		Assert.Null(error);
	}

	[Fact]
	public void Explicit_guild_target_retains_the_id()
	{
		Assert.True(CommandRegistration.TryParse(new[] { "commands", "register", "--guild", "123" }, out var guildId, out var error));
		Assert.Equal((ulong)123, guildId);
		Assert.Null(error);
	}
}
