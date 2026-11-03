using Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Roles;
using Orabot.Tests.TestDoubles;

namespace Orabot.Tests.Roles;

internal sealed class RolesFixture
{
	public const ulong GuildId = 100, MemberId = 200, BotId = 300, TargetId = 30, BotRoleId = 20;
	public IGuild Guild { get; }
	public IGuildUser Member { get; }
	public IGuildUser Bot { get; }
	public IRole Target { get; }
	public ITextChannel Channel { get; }
	public HashSet<ulong> MemberRoles { get; } = new();
	public List<string> Writes { get; } = new();
	public TaskCompletionSource RemovalFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
	public int UserDownloads { get; private set; }
	public int BotDownloads { get; private set; }
	public int WriteAttempts { get; private set; }
	public GuildPermission BotPermissions { get; set; } = GuildPermission.ManageRoles;
	public int TargetPosition { get; set; } = 1;
	public bool TargetManaged { get; set; }
	public bool TargetForeign { get; set; }
	public string TargetName { get; set; } = "Players";
	public bool MemberIsBot { get; set; }
	public Exception? WriteFailure { get; set; }
	public RequestOptions? LastWriteOptions { get; private set; }

	public RolesFixture()
	{
		IRole[] roles = Array.Empty<IRole>();
		Guild = DiscordTestProxy.Create<IGuild>((method, args) => method switch
		{
			"get_Id" => GuildId,
			"get_OwnerId" => (ulong)999,
			"get_Roles" => roles,
			"GetRole" => roles.FirstOrDefault(x => x.Id == (ulong)args![0]!),
			"GetRoleAsync" => Task.FromResult(roles.FirstOrDefault(x => x.Id == (ulong)args![0]!)!),
			"GetCurrentUserAsync" => GetBot(args),
			"GetUserAsync" => GetMember(args),
			_ => throw new NotSupportedException(method)
		});
		var foreignGuild = DiscordTestProxy.Create<IGuild>((method, _) => method == "get_Id" ? (ulong)999 : throw new NotSupportedException(method));
		Target = DiscordTestProxy.Create<IRole>((method, _) => method switch
		{
			"get_Id" => TargetId, "get_Guild" => TargetForeign ? foreignGuild : Guild,
			"get_Name" => TargetName, "get_Position" => TargetPosition, "get_IsManaged" => TargetManaged,
			_ => throw new NotSupportedException(method)
		});
		IRole Role(ulong id, int position) => DiscordTestProxy.Create<IRole>((method, _) => method switch
		{
			"get_Id" => id, "get_Guild" => Guild, "get_Name" => "fixture-role",
			"get_Position" => position, "get_IsManaged" => false,
			_ => throw new NotSupportedException(method)
		});
		roles = new[] { Role(GuildId, 0), Role(BotRoleId, 10), Target };
		Bot = DiscordTestProxy.Create<IGuildUser>((method, _) => method switch
		{
			"get_Id" => BotId, "get_GuildId" => GuildId, "get_Guild" => Guild, "get_IsBot" => true,
			"get_RoleIds" => new[] { BotRoleId }, "get_GuildPermissions" => new GuildPermissions((ulong)BotPermissions),
			_ => throw new NotSupportedException(method)
		});
		Member = DiscordTestProxy.Create<IGuildUser>((method, args) => method switch
		{
			"get_Id" => MemberId, "get_GuildId" => GuildId, "get_Guild" => Guild, "get_IsBot" => MemberIsBot,
			"get_RoleIds" => MemberRoles.ToArray(), "get_GuildPermissions" => GuildPermissions.None,
			"AddRoleAsync" => ChangeRole(args!, true), "RemoveRoleAsync" => ChangeRole(args!, false),
			_ => throw new NotSupportedException(method)
		});
		Channel = DiscordTestProxy.Create<ITextChannel>((method, _) => method switch
		{
			"get_Guild" => Guild, "get_GuildId" => GuildId, "get_Id" => (ulong)500,
			_ => throw new NotSupportedException(method)
		});
	}

	public RoleAssignmentService Service(string names = "Players", string mappings = "ok:Players", string message = "123") =>
		new(Options.Create(new RolesOptions { FreelyAssignedRoles = names, FreelyAssignedRolesByEmote = mappings, RoleAssignmentMessageId = message }),
			NullLogger<RoleAssignmentService>.Instance);

	Task<IGuildUser> GetBot(object?[]? args)
	{
		BotDownloads++;
		((RequestOptions)args![1]!).CancelToken.ThrowIfCancellationRequested();
		return Task.FromResult(Bot);
	}

	Task<IGuildUser> GetMember(object?[]? args)
	{
		UserDownloads++;
		((RequestOptions)args![2]!).CancelToken.ThrowIfCancellationRequested();
		return Task.FromResult((ulong)args[0]! == MemberId ? Member : null!);
	}

	Task ChangeRole(object?[] args, bool add)
	{
		WriteAttempts++;
		LastWriteOptions = (RequestOptions)args[1]!;
		LastWriteOptions.CancelToken.ThrowIfCancellationRequested();
		if (WriteFailure != null) return Task.FromException(WriteFailure);
		var id = args[0] is IRole role ? role.Id : (ulong)args[0]!;
		Writes.Add((add ? "add:" : "remove:") + id);
		if (add) MemberRoles.Add(id); else { MemberRoles.Remove(id); RemovalFinished.TrySetResult(); }
		return Task.CompletedTask;
	}
}
