using Orabot.Bot.Features.Documentation.Models;
using Refit;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Retrieves cancellable OpenRA documentation search indexes for release, playtest and development.</summary>
	public interface IDocsApi
	{
		[Get("/{version}/search/search_index.json")]
		Task<DocsSearchIndex> GetSearchIndex(string version, CancellationToken cancellationToken = default);
	}
}
