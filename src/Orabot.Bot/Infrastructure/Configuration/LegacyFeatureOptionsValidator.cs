#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Options;

namespace Orabot.Bot.Infrastructure.Configuration
{
	public sealed class LegacyFeatureOptionsValidator : IValidateOptions<LegacyFeatureOptions>
	{
		public ValidateOptionsResult Validate(string? name, LegacyFeatureOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);
			var failures = new List<string>();

			ValidateUrl(options.GitHubIconsBaseUrl, nameof(options.GitHubIconsBaseUrl), failures);
			ValidateUrl(options.OpenRaFaviconUrl, nameof(options.OpenRaFaviconUrl), failures);
			ValidateConfigured(options.TrustedRoles, nameof(options.TrustedRoles), failures);
			ValidateConfigured(options.FreelyAssignedRoles, nameof(options.FreelyAssignedRoles), failures);

			if (!ulong.TryParse(options.RoleAssignmentMessageId, out _))
				failures.Add(nameof(options.RoleAssignmentMessageId));

			if (!IsValidRoleMapping(options.FreelyAssignedRolesByEmote))
				failures.Add(nameof(options.FreelyAssignedRolesByEmote));

			ValidatePath(options.LogStorageFolder, nameof(options.LogStorageFolder), failures);
			ValidatePath(options.ReplayStorageFolder, nameof(options.ReplayStorageFolder), failures);
			ValidatePages(options.Traits, nameof(options.Traits), failures);
			ValidatePages(options.Weapons, nameof(options.Weapons), failures);
			ValidatePages(options.Lua, nameof(options.Lua), failures);
			ValidatePages(options.SpriteSequences, nameof(options.SpriteSequences), failures);

			return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
		}

		static void ValidateConfigured(string? value, string key, List<string> failures)
		{
			// Empty allowlists are valid; a missing key would break the existing Split calls.
			if (value is null)
				failures.Add(key);
		}

		static void ValidateUrl(string? value, string key, List<string> failures)
		{
			if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
				|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
				|| string.IsNullOrEmpty(uri.Host))
				failures.Add(key);
		}

		static void ValidatePages(LegacyDocumentationPageOptions? pages, string section, List<string> failures)
		{
			ValidateUrl(pages?.ReleasePageUrl, section + ":ReleasePageUrl", failures);
			ValidateUrl(pages?.PlaytestPageUrl, section + ":PlaytestPageUrl", failures);
			ValidateUrl(pages?.DevelopmentPageUrl, section + ":DevelopmentPageUrl", failures);
		}

		static void ValidatePath(string? value, string key, List<string> failures)
		{
			if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
			{
				failures.Add(key);
				return;
			}

			try
			{
				// Resolve syntax only. Validation must not create or inspect private storage.
				_ = Path.GetFullPath(value);
			}
			catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
			{
				failures.Add(key);
			}
		}

		static bool IsValidRoleMapping(string? value)
		{
			if (value is null)
				return false;

			// Match the existing parser's outer trim, empty-entry handling and ordinal keys.
			var emotes = new HashSet<string>(StringComparer.Ordinal);
			foreach (var rawEntry in value.Split(';'))
			{
				var entry = rawEntry.Trim();
				if (entry.Length == 0)
					continue;

				var parts = entry.Split(':');
				if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0])
					|| string.IsNullOrWhiteSpace(parts[1]) || !emotes.Add(parts[0]))
					return false;
			}

			return true;
		}
	}
}
