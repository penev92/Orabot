using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;

namespace Orabot.Bot.Discord.Logging
{
	internal sealed class LogEventHandler : ILogEventHandler
	{
		private readonly ILogger<LogEventHandler> _logger;

		public LogEventHandler(ILogger<LogEventHandler> logger) => _logger = logger;

		public Task Log(LogMessage message)
		{
			var level = message.Severity switch
			{
				LogSeverity.Critical => LogLevel.Critical,
				LogSeverity.Error => LogLevel.Error,
				LogSeverity.Warning => LogLevel.Warning,
				LogSeverity.Info => LogLevel.Information,
				LogSeverity.Verbose => LogLevel.Debug,
				_ => LogLevel.Trace
			};

			// SDK message/exception text can contain REST responses or gateway payloads.
			// Record safe diagnostic metadata without forwarding tokens or user content.
			_logger.Log(level, "Discord SDK event from {DiscordSource}; exception type: {ExceptionType}.",
				message.Source, message.Exception?.GetType().FullName ?? "none");
			return Task.CompletedTask;
		}
	}
}
