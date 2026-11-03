using System;
using System.Linq;
using Discord;

namespace Orabot.Bot.Features.General
{
	/// <summary>Preserves the configured role-name allowlist for speaking and custom quoting commands.</summary>
	public sealed class TrustedRoleOptions
	{
		public string TrustedRoles { get; set; } = string.Empty;

		public bool IsTrusted(IGuildUser user, IGuild guild)
		{
			if (user == null || guild == null || user.GuildId != guild.Id)
				return false;
			var names = (TrustedRoles ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			return guild.Roles.Any(role => user.RoleIds.Contains(role.Id) && names.Contains(role.Name, StringComparer.Ordinal));
		}
	}
}
