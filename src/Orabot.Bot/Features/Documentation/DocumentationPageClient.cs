using HtmlAgilityPack;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Reads finite HTML responses and extracts headings without assuming exact sibling whitespace.</summary>
	public sealed class DocumentationPageClient : IDocumentationPageClient
	{
		internal const int MaximumDocumentBytes = 4 * 1024 * 1024;
		private readonly HttpClient _client;

		/// <summary>Uses an HTTP client whose transport lifetime is managed by the feature registration.</summary>
		public DocumentationPageClient(HttpClient client) => _client = client;

		/// <summary>Applies a ten-second total request/body deadline and a four-MiB decoded-response budget.</summary>
		public async Task<DocumentationPageEntry> FindEntryAsync(Uri page, string name, CancellationToken cancellationToken)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(TimeSpan.FromSeconds(10));
			var token = deadline.Token;
			using var response = await _client.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			if (response.Content.Headers.ContentLength > MaximumDocumentBytes)
				throw new InvalidDataException("The documentation page exceeds the supported size.");

			using var input = await response.Content.ReadAsStreamAsync(token);
			using var bytes = new MemoryStream();
			var buffer = new byte[8192];
			int count;
			while ((count = await input.ReadAsync(buffer.AsMemory(), token)) != 0)
			{
				if (bytes.Length + count > MaximumDocumentBytes)
					throw new InvalidDataException("The documentation page exceeds the supported size.");
				bytes.Write(buffer, 0, count);
			}
			token.ThrowIfCancellationRequested();
			var document = new HtmlDocument { OptionMaxNestedChildNodes = 256 };
			document.LoadHtml(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
			token.ThrowIfCancellationRequested();
			var heading = document.DocumentNode.Descendants("h3")
				.FirstOrDefault(node => node.GetAttributeValue("id", string.Empty).Equals(name, StringComparison.OrdinalIgnoreCase));
			if (heading == null)
				return new DocumentationPageEntry(false, null);

			for (var sibling = heading.NextSibling; sibling != null; sibling = sibling.NextSibling)
			{
				if (sibling.Name.Length == 2 && sibling.Name[0] == 'h' && char.IsDigit(sibling.Name[1]))
					break;
				var paragraph = sibling.Name == "p" ? sibling : sibling.Descendants("p").FirstOrDefault();
				if (paragraph != null)
					return new DocumentationPageEntry(true, HtmlEntity.DeEntitize(paragraph.InnerText));
			}
			return new DocumentationPageEntry(true, null);
		}
	}
}
