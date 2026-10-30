using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Logging;
using Orabot.Bot.Discord.Logging;
using Xunit;

namespace Orabot.Tests.Discord
{
	public class LogEventHandlerTests
	{
		[Theory]
		[InlineData(LogSeverity.Critical, LogLevel.Critical)]
		[InlineData(LogSeverity.Error, LogLevel.Error)]
		[InlineData(LogSeverity.Warning, LogLevel.Warning)]
		[InlineData(LogSeverity.Info, LogLevel.Information)]
		[InlineData(LogSeverity.Verbose, LogLevel.Debug)]
		[InlineData(LogSeverity.Debug, LogLevel.Trace)]
		public async Task PreservesSeverityWithoutForwardingRawSdkContent(LogSeverity severity, LogLevel expected)
		{
			var logger = new RecordingLogger();
			var handler = new LogEventHandler(logger);
			await handler.Log(new LogMessage(severity, "Gateway", "synthetic-private-payload", new Exception("synthetic-token")));
			var entry = Assert.Single(logger.Entries);
			Assert.Equal(expected, entry.Level);
			Assert.Contains("Gateway", entry.Message);
			Assert.Contains(typeof(Exception).FullName!, entry.Message);
			Assert.DoesNotContain("synthetic-private-payload", entry.Message);
			Assert.DoesNotContain("synthetic-token", entry.Message);
			Assert.Null(entry.Exception);
		}

		private sealed class RecordingLogger : ILogger<LogEventHandler>
		{
			public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
			public bool IsEnabled(LogLevel logLevel) => true;
			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
				=> Entries.Add((logLevel, formatter(state, exception), exception));
		}
	}
}
