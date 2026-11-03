using System;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace Orabot.Bot.Features.Logs
{
	internal class AttachmentLogFileToMessageTransformer
	{
		private readonly string _logStorageFolder;

		public AttachmentLogFileToMessageTransformer(IConfiguration configuration)
		{
			_logStorageFolder = configuration["LogStorageFolder"];
		}

		internal string CreateRawMessage(global::Discord.IAttachment attachment, out string fullText)
		{
			var filePath = Path.Combine(_logStorageFolder, $"{Guid.NewGuid()}_{attachment.Filename}");

			using var webClient = new WebClient();
			webClient.DownloadFile(attachment.Url, filePath);

			fullText = File.ReadAllText(filePath);
			return string.IsNullOrWhiteSpace(fullText) ? null : $"```{fullText[..Math.Min(1000, fullText.Length)]}```";
		}

		internal bool TryCreateExceptionExplanationMessage(string text, out string explanationMessage)
		{
			explanationMessage = null;

			var lines = text.Replace("\r\n", "\n").Split("\n");
			var exceptionLine = lines.LastOrDefault(x => x.Trim().StartsWith("Exception of type "));
			if (exceptionLine == null || exceptionLine.IndexOf(": ", StringComparison.Ordinal) < 0)
				return false;

			exceptionLine = exceptionLine.Substring(exceptionLine.IndexOf(": ", StringComparison.Ordinal) + 2);

			explanationMessage = $"Now, I'm no expert, but I suspect your problem is *probably*  related to this:\r\n> **{exceptionLine}**";


			return true;
		}

		private static bool IsOpenRAStackTraceLine(string line)
		{
			return line.StartsWith("   at OpenRA");
		}

		private static string ExtractPointOfInterest(string line)
		{
			try
			{
				line = line.Substring(line.IndexOf("   at ") + "   at ".Length);
				return line.EndsWith(")") ? line : line.Substring(0, line.IndexOf(") ") + 1);
			}
			catch (Exception)
			{
				return string.Empty;
			}
		}
	}
}
