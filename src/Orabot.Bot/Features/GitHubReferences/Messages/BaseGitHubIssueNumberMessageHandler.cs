using System.Threading;
﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Options;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Features.GitHubReferences.Models;
using RestSharp;

namespace Orabot.Bot.Features.GitHubReferences.Messages
{
	internal abstract class BaseGitHubIssueNumberMessageHandler : BaseNumberParsingMessageHandler
	{
		private const string BaseApiUrl = "https://api.github.com";

		private const string ApiIssueRequestTemplate = "repos/{RepositoryOwner}/{RepositoryName}/issues/{number}";

		private const string ApiPullRequestTemplate = "repos/{RepositoryOwner}/{RepositoryName}/pulls/{number}";

		protected abstract string RepositoryOwner { get; }

		protected abstract string RepositoryName { get; }

		private readonly string _issueIconBaseUrl;

		private readonly IRestClient _restClient;
		private readonly Dictionary<string, Color> _colorPerStatus = new Dictionary<string, Color>
		{
			{ "open", Color.Green },
			{ "closed", Color.Red },
			{ "merged", Color.Purple }
		};

		internal BaseGitHubIssueNumberMessageHandler(IRestClient restClient, IOptions<GitHubReferenceOptions> options)
		{
			_restClient = restClient;

			_issueIconBaseUrl = options.Value.GitHubIconsBaseUrl;
		}

		public override async Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default)
		{
			foreach (var number in GetMatchedNumbers(message.Content))
			{
				var request = new RestRequest($"{BaseApiUrl}/{ApiIssueRequestTemplate}");
				request.AddUrlSegment("RepositoryOwner", RepositoryOwner);
				request.AddUrlSegment("RepositoryName", RepositoryName);
				request.AddUrlSegment("number", number);

				var issue = await _restClient.GetAsync<GitHubIssueResponse>(request, cancellationToken);
				if (issue?.HtmlUrl == null)
				{
					continue;
				}

				var isIssue = issue.PullRequest == null;
				var type = isIssue ? "Issue" : "Pull request";
				var labels = string.Join(", ", issue.Labels?.Select(x => x.Name) ?? Enumerable.Empty<string>());
				var embedFields = new List<EmbedFieldBuilder>();
				if (!string.IsNullOrEmpty(labels))
				{
					embedFields.Add(new EmbedFieldBuilder
					{
						Name = "Labels:",
						Value = labels.Length > 1024 ? labels[..1024] : labels,
						IsInline = true
					});
				}

				var status = issue.State;

				if (!isIssue && status == "closed")
				{
					var pullRequest = new RestRequest($"{BaseApiUrl}/{ApiPullRequestTemplate}");
					pullRequest.AddUrlSegment("RepositoryOwner", RepositoryOwner);
					pullRequest.AddUrlSegment("RepositoryName", RepositoryName);
					pullRequest.AddUrlSegment("number", number);

					var pull = await _restClient.GetAsync<GitHubPullRequestResponse>(pullRequest, cancellationToken);
					if (pull != null)
					{
						embedFields.Add(new EmbedFieldBuilder
						{
							Name = "Status:",
							Value = pull.IsMerged ? "merged" : pull.MergeableState ?? "unknown",
							IsInline = true
						});

						status = pull.IsMerged ? "merged" : issue.State;
						if (pull.IsMerged)
						{
							embedFields.Add(new EmbedFieldBuilder
							{
								Name = "Merged by:",
								Value = pull.MergedBy?.LoginName ?? "unknown",
								IsInline = true
							});
						}
					}
				}

				var body = issue.Body ?? string.Empty;
				var description = body.Length > 250 ? body[..250] + "..." : body;
				var embed = new EmbedBuilder
				{
					Title = issue.Title?.Length > 256 ? issue.Title[..256] : issue.Title,
					ThumbnailUrl = issue.User?.AvatarUrl,
					Url = issue.HtmlUrl,
					Description = description,
					Author = new EmbedAuthorBuilder
					{
						Name = $"{type} #{number} by {issue.User?.LoginName}  ({status})",
						IconUrl = GetIssueIconUrl(isIssue, status),
						Url = issue.HtmlUrl
					},
					Fields = embedFields,
					Footer = new EmbedFooterBuilder
					{
						Text = $"Created at {issue.CreatedAt}"
					},
					Timestamp = issue.UpdatedAt,
					Color = _colorPerStatus.TryGetValue(status ?? string.Empty, out var color) ? color : null
				};

				await message.Channel.SendMessageAsync("", embed: embed.Build(), allowedMentions: AllowedMentions.None, options: new RequestOptions { CancelToken = cancellationToken });
			}
		}

		private string GetIssueIconUrl(bool isIssue, string status)
		{
			return $"{_issueIconBaseUrl}/github-{(isIssue ? "issue" : "pr")}-{status}.png";
		}
	}
}
