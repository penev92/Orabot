using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Checks documentation links at startup and reports configuration keys rather than private values.</summary>
	public sealed class DocumentationLinksOptionsValidator : IValidateOptions<DocumentationLinksOptions>
	{
		/// <summary>Validates every required page and icon URL without making requests.</summary>
		public ValidateOptionsResult Validate(string name, DocumentationLinksOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);
			var failures = new List<string>();
			ValidateUrl(options.OpenRaFaviconUrl, "OpenRaFaviconUrl", failures);
			foreach (var kind in Enum.GetValues<DocumentationKind>())
			{
				var pages = options.GetPages(kind);
				ValidateUrl(pages?.ReleasePageUrl, kind + ":ReleasePageUrl", failures);
				ValidateUrl(pages?.PlaytestPageUrl, kind + ":PlaytestPageUrl", failures);
				ValidateUrl(pages?.DevelopmentPageUrl, kind + ":DevelopmentPageUrl", failures);
			}
			return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
		}

		private static void ValidateUrl(string value, string key, List<string> failures)
		{
			if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
				|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
				|| string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
				failures.Add(key);
		}
	}
}
