using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orabot.Bot.Features.Roles
{
	/// <summary>The outcome of a checked self-assignment request.</summary>
	public enum RoleAssignmentResult { Added, Removed, InvalidMember, NotAllowed, UnsafeRole, CannotManage, Failed }

	/// <summary>Shares allowlist, guild and bot-hierarchy checks between slash commands and reactions.</summary>
	public sealed class RoleAssignmentService
	{
		private readonly HashSet<string> _allowed;
		private readonly IReadOnlyDictionary<string, string> _mappings;
		private readonly ILogger<RoleAssignmentService> _logger;

		public RoleAssignmentService(IOptions<RolesOptions> options, ILogger<RoleAssignmentService> logger)
		{
			var settings = options.Value;
			var names = settings.FreelyAssignedRoles.Split(';').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
			RoleNames = Array.AsReadOnly(names);
			_allowed = new(names, StringComparer.Ordinal);
			RolesOptionsValidator.TryParseMappings(settings.FreelyAssignedRolesByEmote, out var mappings);
			_mappings = mappings;
			AssignmentMessageId = ulong.Parse(settings.RoleAssignmentMessageId, CultureInfo.InvariantCulture);
			_logger = logger;
		}

		public IReadOnlyList<string> RoleNames { get; }
		public ulong AssignmentMessageId { get; }
		public bool TryGetReactionRole(string emoteName, out string roleName) => _mappings.TryGetValue(emoteName, out roleName);

		public Task<RoleAssignmentResult> ToggleAsync(IGuild guild, IGuildUser member, IRole role, CancellationToken token) =>
			ApplyAsync(guild, member, role, null, token);

		public Task<RoleAssignmentResult> SetAsync(IGuild guild, IGuildUser member, IRole role, bool add, CancellationToken token) =>
			ApplyAsync(guild, member, role, add, token);

		private async Task<RoleAssignmentResult> ApplyAsync(IGuild guild, IGuildUser member, IRole role, bool? requestedState, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();
			if (guild == null || member == null || member.GuildId != guild.Id || member.IsBot)
				return RoleAssignmentResult.InvalidMember;
			if (role == null || role.Guild?.Id != guild.Id) return RoleAssignmentResult.UnsafeRole;
			var target = guild.GetRole(role.Id);
			if (target == null || target.Guild?.Id != guild.Id || target.Id == guild.Id || target.IsManaged) return RoleAssignmentResult.UnsafeRole;
			// The original reaction mapping is its own explicit authorization list;
			// mapped roles do not also need to be selectable through /role.
			var allowed = requestedState.HasValue
				? _mappings.Values.Contains(target.Name, StringComparer.Ordinal)
				: _allowed.Contains(target.Name);
			if (!allowed) return RoleAssignmentResult.NotAllowed;

			var hasRole = member.RoleIds.Contains(target.Id);
			var add = requestedState ?? !hasRole;
			// Reaction events express an explicit desired state. Their cached member may
			// lag a preceding change, so always send the idempotent PUT/DELETE.
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
			budget.CancelAfter(TimeSpan.FromSeconds(10));
			try
			{
				var request = new RequestOptions { CancelToken = budget.Token, RetryMode = RetryMode.RetryRatelimit };
				var bot = await guild.GetCurrentUserAsync(CacheMode.AllowDownload, request);
				if (bot == null || bot.GuildId != guild.Id
					|| !(bot.GuildPermissions.ManageRoles || bot.GuildPermissions.Administrator || bot.Id == guild.OwnerId))
					return RoleAssignmentResult.CannotManage;
				var highest = guild.Roles.Where(x => x.Id != guild.Id && bot.RoleIds.Contains(x.Id))
					.OrderByDescending(x => x.Position).ThenBy(x => x.Id).FirstOrDefault();
				if (highest == null || highest.Position < target.Position
					|| (highest.Position == target.Position && highest.Id >= target.Id))
					return RoleAssignmentResult.CannotManage;

				budget.Token.ThrowIfCancellationRequested();
				request.AuditLogReason = add ? "User asked for role." : "User asked that role be removed.";
				if (add) await member.AddRoleAsync(target, request);
				else await member.RemoveRoleAsync(target, request);
				return add ? RoleAssignmentResult.Added : RoleAssignmentResult.Removed;
			}
			catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
			catch (Exception exception)
			{
				_logger.LogWarning("Role assignment failed ({ExceptionType}).", exception.GetType().FullName);
				return RoleAssignmentResult.Failed;
			}
		}
	}
}
