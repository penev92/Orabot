#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orabot.Bot.Infrastructure.Configuration;
using Xunit;

namespace Orabot.Tests.Configuration
{
	/// <summary>
	/// Tests the temporary startup-validation bridge for feature settings still read through
	/// <see cref="IConfiguration"/>. Synthetic settings cover compatibility and key-only errors
	/// without exposing private values. These cases move to feature-specific option tests as
	/// their owning features are migrated and the legacy bridge is removed.
	/// </summary>
	public sealed class LegacyFeatureOptionsTests
	{
		readonly LegacyFeatureOptionsValidator validator = new();

		[Fact]
		public void BindsAndAcceptsCompleteSyntheticConfiguration()
		{
			var options = Bind(ValidConfiguration());

			Assert.Equal("Trusted", options.TrustedRoles);
			Assert.Equal("0", options.RoleAssignmentMessageId);
			Assert.Equal("https://example.org/traits/release", options.Traits.ReleasePageUrl);
			Assert.Equal("https://example.org/sprite-sequences/development", options.SpriteSequences.DevelopmentPageUrl);
			Assert.True(validator.Validate(null, options).Succeeded);
		}

		[Fact]
		public void AllowsConfiguredEmptyAllowlists()
		{
			var configuration = ValidConfiguration();
			configuration["TrustedRoles"] = "";
			configuration["FreelyAssignedRoles"] = "";
			configuration["FreelyAssignedRolesByEmote"] = "";

			Assert.True(validator.Validate(null, Bind(configuration)).Succeeded);
		}

		[Theory]
		[InlineData("TrustedRoles")]
		[InlineData("FreelyAssignedRoles")]
		[InlineData("FreelyAssignedRolesByEmote")]
		public void RejectsMissingAllowlistKey(string key)
		{
			var configuration = ValidConfiguration();
			configuration.Remove(key);

			AssertOnlyFailure(key, validator.Validate(null, Bind(configuration)));
		}

		[Theory]
		[InlineData("0")]
		[InlineData("18446744073709551615")]
		[InlineData(" 42 ")]
		public void AcceptsUnsignedMessageIdentifiersIncludingZero(string value)
		{
			var configuration = ValidConfiguration();
			configuration["RoleAssignmentMessageId"] = value;

			Assert.True(validator.Validate(null, Bind(configuration)).Succeeded);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("private-invalid-id")]
		[InlineData("-1")]
		[InlineData("18446744073709551616")]
		public void RejectsInvalidMessageIdentifiersWithoutIncludingTheirValues(string? value)
		{
			var configuration = ValidConfiguration();
			configuration["RoleAssignmentMessageId"] = value;

			AssertOnlyFailure("RoleAssignmentMessageId", validator.Validate(null, Bind(configuration)));
		}

		[Theory]
		[InlineData("missing-separator")]
		[InlineData(":Players")]
		[InlineData("happy:")]
		[InlineData("happy:Players:Other")]
		[InlineData("happy:Players;happy:Helpers")]
		[InlineData("   :Players")]
		[InlineData("happy:  ")]
		public void RejectsMalformedOrDuplicateEmoteMappings(string value)
		{
			var configuration = ValidConfiguration();
			configuration["FreelyAssignedRolesByEmote"] = value;

			AssertOnlyFailure("FreelyAssignedRolesByEmote", validator.Validate(null, Bind(configuration)));
		}

		[Fact]
		public void PreservesOrdinalEmoteKeysAndIgnoresEmptyMappingEntries()
		{
			var configuration = ValidConfiguration();
			configuration["FreelyAssignedRolesByEmote"] = " ; happy:Players ; ; HAPPY:Helpers ; ";

			Assert.True(validator.Validate(null, Bind(configuration)).Succeeded);
		}

		[Theory]
		[MemberData(nameof(UrlKeys))]
		public void ValidatesEveryConfiguredUrlWithoutIncludingItsValue(string key)
		{
			var configuration = ValidConfiguration();
			configuration[key] = "private-invalid-url";

			AssertOnlyFailure(key, validator.Validate(null, Bind(configuration)));
		}

		[Theory]
		[InlineData("http://example.org/icons/")]
		[InlineData("https://example.org/icons/")]
		public void AllowsHttpAndHttpsUrls(string value)
		{
			var configuration = ValidConfiguration();
			configuration["GitHubIconsBaseUrl"] = value;

			Assert.True(validator.Validate(null, Bind(configuration)).Succeeded);
		}

		[Theory]
		[InlineData(null)]
		[InlineData("")]
		[InlineData("relative/icons")]
		[InlineData("ftp://example.org/icons/")]
		public void RejectsMissingRelativeOrNonHttpUrls(string? value)
		{
			var configuration = ValidConfiguration();
			configuration["GitHubIconsBaseUrl"] = value;

			AssertOnlyFailure("GitHubIconsBaseUrl", validator.Validate(null, Bind(configuration)));
		}

		[Theory]
		[MemberData(nameof(InvalidStoragePaths))]
		public void RejectsInvalidStoragePathsWithoutIncludingTheirValues(string key, string? value)
		{
			var configuration = ValidConfiguration();
			configuration[key] = value;

			AssertOnlyFailure(key, validator.Validate(null, Bind(configuration)));
		}

		[Fact]
		public void AllowsRelativeAndAbsolutePathsWithoutCreatingStorageDirectories()
		{
			var configuration = ValidConfiguration();
			var relative = Path.Combine("uncreated-test-storage", Guid.NewGuid().ToString("N"));
			var absolute = Path.GetFullPath(Path.Combine("uncreated-test-storage", Guid.NewGuid().ToString("N")));
			configuration["LogStorageFolder"] = relative;
			configuration["ReplayStorageFolder"] = absolute;
			Assert.False(Directory.Exists(relative));
			Assert.False(Directory.Exists(absolute));

			Assert.True(validator.Validate(null, Bind(configuration)).Succeeded);

			Assert.False(Directory.Exists(relative));
			Assert.False(Directory.Exists(absolute));
		}

		[Fact]
		public void ReturnsOnlyConfigurationKeysForMultipleFailures()
		{
			var configuration = ValidConfiguration();
			configuration["RoleAssignmentMessageId"] = "private-id";
			configuration["FreelyAssignedRolesByEmote"] = "private-mapping";
			configuration["Traits:ReleasePageUrl"] = "private-url";
			configuration["ReplayStorageFolder"] = "private-path\0";

			var result = validator.Validate(null, Bind(configuration));

			Assert.True(result.Failed);
			Assert.Equal(new[] { "FreelyAssignedRolesByEmote", "ReplayStorageFolder", "RoleAssignmentMessageId", "Traits:ReleasePageUrl" },
				result.Failures.OrderBy(key => key, StringComparer.Ordinal));
		}

		public static IEnumerable<object[]> UrlKeys()
		{
			yield return new object[] { "GitHubIconsBaseUrl" };
			yield return new object[] { "OpenRaFaviconUrl" };
			foreach (var section in new[] { "Traits", "Weapons", "Lua", "SpriteSequences" })
				foreach (var page in new[] { "ReleasePageUrl", "PlaytestPageUrl", "DevelopmentPageUrl" })
					yield return new object[] { section + ":" + page };
		}

		public static IEnumerable<object?[]> InvalidStoragePaths()
		{
			foreach (var key in new[] { "LogStorageFolder", "ReplayStorageFolder" })
				foreach (var value in new[] { null, "", " ", "private-path\0" })
					yield return new object?[] { key, value };
		}

		static LegacyFeatureOptions Bind(Dictionary<string, string?> values)
		{
			var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
			return configuration.Get<LegacyFeatureOptions>()!;
		}

		static Dictionary<string, string?> ValidConfiguration()
		{
			var values = new Dictionary<string, string?>
			{
				["GitHubIconsBaseUrl"] = "https://example.org/icons/",
				["OpenRaFaviconUrl"] = "https://example.org/favicon.ico",
				["TrustedRoles"] = "Trusted",
				["RoleAssignmentMessageId"] = "0",
				["FreelyAssignedRolesByEmote"] = "happy:Players;helper:Helpers",
				["FreelyAssignedRoles"] = "Players;Helpers",
				["LogStorageFolder"] = "synthetic-data/logs",
				["ReplayStorageFolder"] = "synthetic-data/replays"
			};
			foreach (var section in new[] { "Traits", "Weapons", "Lua", "SpriteSequences" })
			{
				var segment = section == "SpriteSequences" ? "sprite-sequences" : section.ToLowerInvariant();
				values[section + ":ReleasePageUrl"] = "https://example.org/" + segment + "/release";
				values[section + ":PlaytestPageUrl"] = "https://example.org/" + segment + "/playtest";
				values[section + ":DevelopmentPageUrl"] = "https://example.org/" + segment + "/development";
			}
			return values;
		}

		static void AssertOnlyFailure(string key, ValidateOptionsResult result)
		{
			Assert.True(result.Failed);
			Assert.Equal(key, Assert.Single(result.Failures));
		}
	}
}
