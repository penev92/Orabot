using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Models;

namespace Orabot.Tests.Documentation;

/// <summary>Verifies catalog reads and bounded suggestion behavior without a worker or HTTP dependency.</summary>
public sealed class DocumentationCatalogTests
{
	[Fact]
	public void Cold_catalog_returns_no_suggestions_for_every_family_and_version()
	{
		var catalog = new DocumentationCatalog();
		foreach (var kind in Enum.GetValues<DocumentationKind>())
			foreach (var version in new[] { "release", "playtest", "development" })
				Assert.Empty(catalog.Suggest(kind, version, ""));
	}

	[Fact]
	public void Suggestions_filter_case_insensitively_and_enforce_choice_budgets()
	{
		var entries = Enumerable.Range(0, 50).ToDictionary(index => $"Fixture{index:D2}", index => new DocsEntry { Title = $"Fixture{index:D2}" });
		entries[new string('a', 101)] = new DocsEntry { Title = new string('a', 101) };
		var catalog = new DocumentationCatalog();
		catalog.Publish(new DocumentationSnapshot(new ParsedDocs { Traits = entries }, ParsedDocs.Empty, ParsedDocs.Empty));
		var suggestions = catalog.Suggest(DocumentationKind.Traits, "release", "fIXtURE");
		Assert.Equal(25, suggestions.Count);
		Assert.All(suggestions, item =>
		{
			Assert.InRange(item.Name.Length, 1, 100);
			Assert.InRange(item.Value.Length, 1, 100);
		});
		Assert.Empty(catalog.Suggest(DocumentationKind.Traits, "playtest", "Fixture"));
		Assert.Empty(catalog.Suggest(DocumentationKind.Traits, "unknown", "Fixture"));
		Assert.Empty(catalog.Suggest(DocumentationKind.Traits, "release", new string('a', 101)));
	}

	[Fact]
	public void Unsupported_or_oversized_labels_and_values_are_excluded_without_truncating_identity()
	{
		var entries = new Dictionary<string, DocsEntry>
		{
			[new string('x', 101)] = new() { Title = "ShortValue" },
			["ShortLabel"] = new() { Title = new string('x', 101) },
			["Valid"] = new() { Title = "Valid" }
		};
		var catalog = new DocumentationCatalog();
		catalog.Publish(new DocumentationSnapshot(new ParsedDocs { Traits = entries }, ParsedDocs.Empty, ParsedDocs.Empty));
		Assert.Equal("Valid", Assert.Single(catalog.Suggest(DocumentationKind.Traits, "release", "")).Value);
	}
}
