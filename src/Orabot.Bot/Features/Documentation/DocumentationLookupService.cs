using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>A resolved documentation response ready for presentation, with a safe page fallback.</summary>
	public sealed record DocumentationLookupResult(DocumentationKind Kind, string Name, string TargetUrl, string Description);

	/// <summary>Validates submitted options and coordinates page lookup independently of Discord presentation.</summary>
	public sealed class DocumentationLookupService
	{
		private readonly DocumentationLinksOptions _links;
		private readonly IDocumentationPageClient _pages;

		/// <summary>Receives validated feature links and a replaceable page client.</summary>
		public DocumentationLookupService(IOptions<DocumentationLinksOptions> links, IDocumentationPageClient pages)
		{
			_links = links.Value;
			_pages = pages;
		}

		/// <summary>Recognizes the exact public version-choice values, even when Discord's UI is bypassed.</summary>
		public static bool IsVersionSupported(string version) => version is "release" or "playtest" or "development";

		/// <summary>Rejects oversized/control-character names and unsupported versions before any request.</summary>
		public static bool IsInputValid(string name, string version) => IsVersionSupported(version)
			&& (name == null || (name.Length <= 100 && !name.Any(char.IsControl)));

		/// <summary>Resolves a named heading or the configured family page when no matching entry exists.</summary>
		public async Task<DocumentationLookupResult> LookupAsync(DocumentationKind kind, string name, string version, CancellationToken cancellationToken)
		{
			if (!IsInputValid(name, version))
				throw new ArgumentException("Choose a supported documentation version and a name of at most 100 characters.");
			var page = _links.GetPages(kind).GetPageUrl(version);
			if (string.IsNullOrWhiteSpace(name))
				return new DocumentationLookupResult(kind, null, page, null);
			var entry = await _pages.FindEntryAsync(new Uri(page), name, cancellationToken);
			return new DocumentationLookupResult(kind, entry.IsPresent ? name : null,
				entry.IsPresent ? page + "#" + Uri.EscapeDataString(name.ToLowerInvariant()) : page, entry.Description);
		}
	}
}
