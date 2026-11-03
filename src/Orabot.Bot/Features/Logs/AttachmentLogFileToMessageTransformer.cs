using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Infrastructure.Attachments;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Logs
{
	internal sealed record LogAttachmentText(string RawMessage, string FullText);

	internal sealed class AttachmentLogFileToMessageTransformer
	{
		internal const int MaximumLogBytes = 4 * 1024 * 1024;
		private readonly AttachmentDownloader _downloader;
		private readonly string _storageFolder;

		public AttachmentLogFileToMessageTransformer(AttachmentDownloader downloader, IOptions<LogOptions> options)
		{
			_downloader = downloader;
			_storageFolder = options.Value.LogStorageFolder;
		}

		internal async Task<LogAttachmentText> CreateRawMessageAsync(IAttachment attachment, CancellationToken cancellationToken = default)
		{
			using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			deadline.CancelAfter(AttachmentDownloader.OperationTimeout);
			var archived = await _downloader.DownloadAsync(attachment, _storageFolder, MaximumLogBytes, ".log", deadline.Token);
			var text = await File.ReadAllTextAsync(archived.FilePath, deadline.Token);
			var message = string.IsNullOrWhiteSpace(text) ? null : $"```{text[..Math.Min(1000, text.Length)]}```";
			return new LogAttachmentText(message, text);
		}

		internal bool TryCreateExceptionExplanationMessage(string text, out string explanationMessage, int maximumLength = 2000)
		{
			explanationMessage = null;
			if (string.IsNullOrEmpty(text))
				return false;

			ReadOnlySpan<char> lastException = default;
			foreach (var line in text.AsSpan().EnumerateLines())
				if (line.Trim().StartsWith("Exception of type ", StringComparison.Ordinal))
					lastException = line;
			var separator = lastException.IndexOf(": ", StringComparison.Ordinal);
			if (lastException.IsEmpty || separator < 0)
				return false;

			const string prefix = "Now, I'm no expert, but I suspect your problem is *probably*  related to this:\r\n> **";
			const string suffix = "**";
			var available = maximumLength - prefix.Length - suffix.Length;
			if (available < 3)
				return false;
			var detail = lastException[(separator + 2)..];
			var bounded = detail.Length <= available ? detail.ToString() : detail[..(available - 3)].ToString() + "...";
			explanationMessage = prefix + bounded + suffix;
			return true;
		}
	}
}
