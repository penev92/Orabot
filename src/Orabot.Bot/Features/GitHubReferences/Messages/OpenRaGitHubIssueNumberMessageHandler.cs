using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RestSharp;

namespace Orabot.Bot.Features.GitHubReferences.Messages
{
	internal class OpenRaGitHubIssueNumberMessageHandler : BaseGitHubIssueNumberMessageHandler
	{
		protected override string RepositoryOwner => "OpenRA";

		protected override string RepositoryName => "OpenRA";

		protected override Dictionary<string, int> MinimumHandledNumberPerKeyword { get; } = new Dictionary<string, int>
		{
			{ string.Empty, 2000 },
			{ "ora", 0 }
		};

		public OpenRaGitHubIssueNumberMessageHandler(IRestClient restClient, IOptions<GitHubReferenceOptions> options) : base(restClient, options) { }
	}
}
