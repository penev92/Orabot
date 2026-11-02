#nullable enable

namespace Orabot.Bot.Infrastructure.Configuration
{
	// Transitional validation for keys still read directly by legacy feature constructors.
	public sealed class LegacyFeatureOptions
	{
		public string? GitHubIconsBaseUrl { get; set; }
		public string? TrustedRoles { get; set; }
		public string? RoleAssignmentMessageId { get; set; }
		public string? FreelyAssignedRolesByEmote { get; set; }
		public string? FreelyAssignedRoles { get; set; }
		public string? LogStorageFolder { get; set; }
		public string? ReplayStorageFolder { get; set; }
	}

}
