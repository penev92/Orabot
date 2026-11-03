using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Options;

namespace Orabot.Bot.Features.Roles
{
	/// <summary>Preserves the original root configuration keys for self-assigned and reaction roles.</summary>
	public sealed class RolesOptions
	{
		public string FreelyAssignedRoles { get; set; }
		public string FreelyAssignedRolesByEmote { get; set; }
		public string RoleAssignmentMessageId { get; set; }
	}

	/// <summary>Validates role configuration without echoing configured names or values.</summary>
	public sealed class RolesOptionsValidator : IValidateOptions<RolesOptions>
	{
		public ValidateOptionsResult Validate(string name, RolesOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);
			var failures = new List<string>();
			if (options.FreelyAssignedRoles == null) failures.Add(nameof(options.FreelyAssignedRoles));
			if (!ulong.TryParse(options.RoleAssignmentMessageId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
				failures.Add(nameof(options.RoleAssignmentMessageId));
			if (!TryParseMappings(options.FreelyAssignedRolesByEmote, out _)) failures.Add(nameof(options.FreelyAssignedRolesByEmote));
			return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
		}

		internal static bool TryParseMappings(string value, out Dictionary<string, string> mappings)
		{
			mappings = new(StringComparer.Ordinal);
			if (value == null) return false;
			foreach (var entry in value.Split(';').Select(x => x.Trim()).Where(x => x.Length != 0))
			{
				var parts = entry.Split(':');
				if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])
					|| !mappings.TryAdd(parts[0], parts[1])) return false;
			}
			return true;
		}
	}
}
