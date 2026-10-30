using Orabot.Bot.Features.Maps.Models;
using Refit;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Maps
{
	public interface IMapsApi
	{
		[Get("/api/maps")]
		Task<Dictionary<string, MapInfo>> GetMaps(CancellationToken cancellationToken = default);
	}
}
