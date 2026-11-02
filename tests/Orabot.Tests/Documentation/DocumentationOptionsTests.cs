using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Documentation;

namespace Orabot.Tests.Documentation;

/// <summary>Exercises the configuration keys migrated from the transitional legacy bridge.</summary>
public sealed class DocumentationOptionsTests
{
	[Fact]
	public void Binds_all_existing_page_and_icon_keys()
	{
		var options = Bind(ValidSettings());
		Assert.Equal("https://example.org/traits/release", options.Traits.ReleasePageUrl);
		Assert.Equal("https://example.org/SpriteSequences/development", options.SpriteSequences.DevelopmentPageUrl);
		Assert.True(new DocumentationLinksOptionsValidator().Validate(null!, options).Succeeded);
	}

	[Theory]
	[MemberData(nameof(LinkKeys))]
	public void Reports_only_key_when_any_link_is_invalid(string key)
	{
		var settings = ValidSettings();
		settings[key] = "private-invalid-link-fixture";
		var result = new DocumentationLinksOptionsValidator().Validate(null!, Bind(settings));
		Assert.NotNull(result.Failures);
		Assert.Equal(key, Assert.Single(result.Failures));
	}

	[Theory]
	[InlineData("ftp://example.org/traits")]
	[InlineData("https://private:credential@example.org/traits")]
	[InlineData("relative/traits")]
	public void Rejects_non_http_relative_and_credential_containing_links(string value)
	{
		var settings = ValidSettings();
		settings["Traits:ReleasePageUrl"] = value;
		var result = new DocumentationLinksOptionsValidator().Validate(null!, Bind(settings));
		Assert.NotNull(result.Failures);
		Assert.Equal("Traits:ReleasePageUrl", Assert.Single(result.Failures));
	}

	public static IEnumerable<object[]> LinkKeys()
	{
		yield return new object[] { "OpenRaFaviconUrl" };
		foreach (var kind in Enum.GetValues<DocumentationKind>())
			foreach (var version in new[] { "ReleasePageUrl", "PlaytestPageUrl", "DevelopmentPageUrl" })
				yield return new object[] { kind + ":" + version };
	}

	internal static Dictionary<string, string?> ValidSettings()
	{
		var settings = new Dictionary<string, string?> { ["OpenRaFaviconUrl"] = "https://example.org/favicon.ico" };
		foreach (var kind in Enum.GetValues<DocumentationKind>())
			foreach (var version in new[] { "Release", "Playtest", "Development" })
				settings[kind + ":" + version + "PageUrl"] = "https://example.org/" + (kind == DocumentationKind.Traits ? "traits" : kind.ToString()) + "/" + version.ToLowerInvariant();
		return settings;
	}

	internal static DocumentationLinksOptions Bind(Dictionary<string, string?> values) =>
		new ConfigurationBuilder().AddInMemoryCollection(values).Build().Get<DocumentationLinksOptions>()!;

	internal static IOptions<DocumentationLinksOptions> Links() => Options.Create(Bind(ValidSettings()));
}
