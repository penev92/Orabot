using Discord;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Infrastructure.Attachments
{
	/// <summary>A complete archived attachment; the original filename is display data, never a storage path.</summary>
	public sealed record DownloadedAttachment(string FilePath, string OriginalFileName, long Length);

	/// <summary>Streams Discord attachments into generated archive names with finite time and actual-byte budgets.</summary>
	public sealed class AttachmentDownloader
	{
		public const string ClientName = "DiscordAttachments";
		public static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
		private readonly IHttpClientFactory _clients;

		public AttachmentDownloader(IHttpClientFactory clients) => _clients = clients;

		public async Task<DownloadedAttachment> DownloadAsync(IAttachment attachment, string storageFolder,
			long maximumBytes, string trustedExtension, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(attachment);
			ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
			if (trustedExtension is not (".log" or ".orarep"))
				throw new ArgumentException("Unsupported attachment archive extension.", nameof(trustedExtension));
			if (!IsAllowedUrl(attachment.Url, out var uri))
				throw new InvalidDataException("The attachment URL is not a supported Discord CDN URL.");
			if (attachment.Size < 0 || attachment.Size > maximumBytes)
				throw new InvalidDataException("The attachment exceeds the supported size.");

			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(OperationTimeout);
			var token = deadline.Token;
			using var client = _clients.CreateClient(ClientName);
			using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
			if ((int)response.StatusCode is >= 300 and < 400)
				throw new InvalidDataException("Attachment redirects are not supported.");
			response.EnsureSuccessStatusCode();
			var contentLength = response.Content.Headers.ContentLength;
			if (contentLength > maximumBytes)
				throw new InvalidDataException("The attachment exceeds the supported size.");

			var folder = Path.GetFullPath(storageFolder);
			Directory.CreateDirectory(folder);
			var finalPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + trustedExtension);
			var partialPath = finalPath + ".partial";
			try
			{
				long total = 0;
				await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
					FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
				await using (var input = await response.Content.ReadAsStreamAsync(token))
				{
					var buffer = new byte[81920];
					int count;
					while ((count = await input.ReadAsync(buffer.AsMemory(), token)) != 0)
					{
						if (count > maximumBytes - total)
							throw new InvalidDataException("The attachment exceeds the supported size.");
						total += count;
						await output.WriteAsync(buffer.AsMemory(0, count), token);
					}
					if ((contentLength.HasValue && total != contentLength.Value)
						|| (attachment.Size > 0 && total != attachment.Size))
						throw new InvalidDataException("The attachment length does not match its metadata.");
					await output.FlushAsync(token);
				}
				token.ThrowIfCancellationRequested();
				File.Move(partialPath, finalPath);
				return new DownloadedAttachment(finalPath, attachment.Filename, total);
			}
			finally
			{
				// Failed or cancelled transfers leave no incomplete archive file.
				File.Delete(partialPath);
			}
		}

		internal static bool IsAllowedUrl(string value, out Uri uri) =>
			Uri.TryCreate(value, UriKind.Absolute, out uri)
			&& uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo)
			&& (uri.Host.Equals("cdn.discordapp.com", StringComparison.OrdinalIgnoreCase)
				|| uri.Host.Equals("media.discordapp.net", StringComparison.OrdinalIgnoreCase))
			&& (uri.AbsolutePath.StartsWith("/attachments/", StringComparison.Ordinal)
				|| uri.AbsolutePath.StartsWith("/ephemeral-attachments/", StringComparison.Ordinal));
	}
}
