using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RestSharp;

namespace Orabot.Bot.Features.GitHubReferences.Messages
{
	internal class OpenRaResourceCenterGitHubIssueNumberMessageHandler : BaseGitHubIssueNumberMessageHandler
	{
		protected override string RepositoryOwner => "OpenRA";

		protected override string RepositoryName => "OpenRA-Resources";

		protected override Dictionary<string, int> MinimumHandledNumberPerKeyword { get; } = new Dictionary<string, int>
		{
			{ "resource", 0 },
			{ "rc", 0 }
		};

		public OpenRaResourceCenterGitHubIssueNumberMessageHandler(IRestClient restClient, IOptions<GitHubReferenceOptions> options) : base(restClient, options) { }
	}
}
