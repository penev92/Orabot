using System;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>The matched heading and its first descriptive paragraph, without transport or Discord objects.</summary>
	public sealed record DocumentationPageEntry(bool IsPresent, string Description);

	/// <summary>Fetches a configured page and looks up a heading using bounded, cancellable work.</summary>
	public interface IDocumentationPageClient
	{
		/// <summary>Finds a heading by its HTML identifier; an absent heading is a normal page-level fallback.</summary>
		Task<DocumentationPageEntry> FindEntryAsync(Uri page, string name, CancellationToken cancellationToken);
	}
}
