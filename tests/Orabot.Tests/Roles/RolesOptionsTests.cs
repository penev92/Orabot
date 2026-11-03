using Orabot.Bot.Features.Roles;

namespace Orabot.Tests.Roles;

public sealed class RolesOptionsTests
{
	[Fact]
	public void Empty_allowlists_and_message_zero_are_valid()
	{
		var options = new RolesOptions { FreelyAssignedRoles = "", FreelyAssignedRolesByEmote = "; ;", RoleAssignmentMessageId = "0" };
		Assert.True(new RolesOptionsValidator().Validate(null!, options).Succeeded);
	}

	[Theory]
	[InlineData("private-invalid-id", "ok:Players", "RoleAssignmentMessageId")]
	[InlineData("123", "private:bad:mapping", "FreelyAssignedRolesByEmote")]
	[InlineData("123", "ok:Players;ok:Other", "FreelyAssignedRolesByEmote")]
	public void Failures_contain_only_configuration_keys(string message, string mappings, string key)
	{
		var options = new RolesOptions { FreelyAssignedRoles = "Players", FreelyAssignedRolesByEmote = mappings, RoleAssignmentMessageId = message };
		var result = new RolesOptionsValidator().Validate(null!, options);
		Assert.NotNull(result.Failures);
		Assert.Equal(key, Assert.Single(result.Failures));
	}
}
