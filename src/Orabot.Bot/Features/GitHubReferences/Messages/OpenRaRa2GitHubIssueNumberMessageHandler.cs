using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RestSharp;

namespace Orabot.Bot.Features.GitHubReferences.Messages
{
	internal class OpenRaRa2GitHubIssueNumberMessageHandler : BaseGitHubIssueNumberMessageHandler
	{
		protected override string RepositoryOwner => "OpenRA";

		protected override string RepositoryName => "ra2";

		protected override Dictionary<string, int> MinimumHandledNumberPerKeyword { get; } = new Dictionary<string, int>
		{
			{ "ra2", 0 }
		};

		public OpenRaRa2GitHubIssueNumberMessageHandler(IRestClient restClient, IOptions<GitHubReferenceOptions> options) : base(restClient, options) { }
	}
}
