using Discord;
using Orabot.Bot.Features.Roles;

namespace Orabot.Tests.Roles;

public sealed class RoleAssignmentServiceTests
{
	[Fact]
	public async Task Reaction_mapping_is_an_independent_allowlist_from_slash_self_assignment()
	{
		var fixture = new RolesFixture();
		var service = fixture.Service(names: "", mappings: "ok:Players");
		Assert.Equal(RoleAssignmentResult.NotAllowed,
			await service.ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Empty(fixture.Writes);
		Assert.Equal(RoleAssignmentResult.Added,
			await service.SetAsync(fixture.Guild, fixture.Member, fixture.Target, true, CancellationToken.None));
		Assert.Equal(new[] { "add:30" }, fixture.Writes);
	}

	[Fact]
	public async Task Allowlisted_member_without_manage_roles_can_toggle_own_role()
	{
		var fixture = new RolesFixture();
		var service = fixture.Service();
		Assert.Equal(RoleAssignmentResult.Added, await service.ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Equal(RoleAssignmentResult.Removed, await service.ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Equal(new[] { "add:30", "remove:30" }, fixture.Writes);
		Assert.Equal(RetryMode.RetryRatelimit, fixture.LastWriteOptions!.RetryMode);
	}

	[Fact]
	public async Task Role_allowlist_uses_ordinal_names()
	{
		var fixture = new RolesFixture { TargetName = "players" };
		Assert.Equal(RoleAssignmentResult.NotAllowed, await fixture.Service().ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Empty(fixture.Writes);
		Assert.Equal(0, fixture.BotDownloads);
	}

	[Theory]
	[InlineData(true, false, 1)]
	[InlineData(false, true, 1)]
	[InlineData(false, false, 11)]
	public async Task Managed_foreign_or_higher_roles_are_rejected(bool managed, bool foreign, int position)
	{
		var fixture = new RolesFixture { TargetManaged = managed, TargetForeign = foreign, TargetPosition = position };
		var result = await fixture.Service().ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None);
		Assert.Contains(result, new[] { RoleAssignmentResult.UnsafeRole, RoleAssignmentResult.CannotManage });
		Assert.Empty(fixture.Writes);
	}

	[Fact]
	public async Task Everyone_and_missing_bot_permissions_are_rejected()
	{
		var fixture = new RolesFixture();
		var everyone = fixture.Guild.GetRole(RolesFixture.GuildId);
		Assert.Equal(RoleAssignmentResult.UnsafeRole, await fixture.Service("fixture-role").ToggleAsync(fixture.Guild, fixture.Member, everyone, CancellationToken.None));
		fixture.BotPermissions = 0;
		Assert.Equal(RoleAssignmentResult.CannotManage, await fixture.Service().ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Empty(fixture.Writes);
	}

	[Fact]
	public async Task Administrator_bot_permissions_are_effective_and_same_position_uses_role_id_order()
	{
		var fixture = new RolesFixture { BotPermissions = GuildPermission.Administrator, TargetPosition = 10 };
		Assert.Equal(RoleAssignmentResult.Added, await fixture.Service().ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
	}

	[Fact]
	public async Task Reaction_state_is_idempotent_and_cancellation_prevents_writes()
	{
		var fixture = new RolesFixture();
		var service = fixture.Service();
		Assert.Equal(RoleAssignmentResult.Removed, await service.SetAsync(fixture.Guild, fixture.Member, fixture.Target, false, CancellationToken.None));
		Assert.Equal(RoleAssignmentResult.Removed, await service.SetAsync(fixture.Guild, fixture.Member, fixture.Target, false, CancellationToken.None));
		Assert.Empty(fixture.MemberRoles);
		Assert.Equal(2, fixture.BotDownloads);
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, new CancellationToken(true)));
		Assert.Equal(new[] { "remove:30", "remove:30" }, fixture.Writes);
	}

	[Fact]
	public async Task Ambiguous_write_failure_is_not_retried()
	{
		var fixture = new RolesFixture { WriteFailure = new InvalidOperationException("synthetic-private-api-payload") };
		Assert.Equal(RoleAssignmentResult.Failed, await fixture.Service().ToggleAsync(fixture.Guild, fixture.Member, fixture.Target, CancellationToken.None));
		Assert.Empty(fixture.Writes);
		Assert.Equal(1, fixture.BotDownloads);
		Assert.Equal(1, fixture.WriteAttempts);
	}
}
