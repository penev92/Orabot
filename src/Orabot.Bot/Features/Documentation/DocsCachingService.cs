using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Documentation.Models;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Refit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation
{
	public sealed record DocumentationSnapshot(ParsedDocs ReleaseDocs, ParsedDocs PlaytestDocs, ParsedDocs DevelopmentDocs);

	public class DocsCachingService : SupervisedBackgroundService
	{
		private readonly IDocsApi _docsApi;
		private readonly TimeProvider _timeProvider;
		private readonly ILogger<DocsCachingService> _logger;
		private readonly TimeSpan _scanInterval;
		private DocumentationSnapshot _snapshot = new DocumentationSnapshot(ParsedDocs.Empty, ParsedDocs.Empty, ParsedDocs.Empty);

		public DocumentationSnapshot Snapshot => Volatile.Read(ref _snapshot);

		public ParsedDocs ReleaseDocs => Snapshot.ReleaseDocs;

		public ParsedDocs PlaytestDocs => Snapshot.PlaytestDocs;

		public ParsedDocs DevelopmentDocs => Snapshot.DevelopmentDocs;

		public DocsCachingService(
			IDocsApi docsApi,
			IOptions<DocumentationCacheOptions> options,
			ILogger<DocsCachingService> logger,
			TimeProvider timeProvider,
			WorkerFailureState failureState) : base(logger, failureState)
		{
			_docsApi = docsApi;
			_timeProvider = timeProvider;
			_logger = logger;
			_scanInterval = TimeSpan.FromMilliseconds(options.Value.ScanInterval);
		}

		protected override async Task ExecuteSupervisedAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					_logger.LogInformation("Updating documentation cache");
					await RefreshAsync(stoppingToken);
					_logger.LogInformation("Documentation cache refreshed");
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested && IsRecoverableRefreshFailure(exception))
				{
					_logger.LogWarning("Documentation refresh failed; retaining the last complete snapshot; exception type: {ExceptionType}",
						exception.GetType().FullName);
				}

				await Task.Delay(_scanInterval, _timeProvider, stoppingToken);
			}
		}

		internal async Task RefreshAsync(CancellationToken cancellationToken)
		{
			var release = await ParseSearchIndex("release", cancellationToken);
			await Task.Delay(TimeSpan.FromSeconds(2), _timeProvider, cancellationToken);
			var playtest = await ParseSearchIndex("playtest", cancellationToken);
			await Task.Delay(TimeSpan.FromSeconds(2), _timeProvider, cancellationToken);
			var development = await ParseSearchIndex("bleed", cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			Volatile.Write(ref _snapshot, new DocumentationSnapshot(release, playtest, development));
		}

		private async Task<ParsedDocs> ParseSearchIndex(string version, CancellationToken cancellationToken)
		{
			var response = await _docsApi.GetSearchIndex(version, cancellationToken);
			if (response?.Docs == null)
				throw new InvalidDataException("The documentation search index contains no document collection.");

			return new ParsedDocs
			{
				Traits = PrepareEntries(response.Docs, "traits"),
				Weapons = PrepareEntries(response.Docs, "weapons"),
				SpriteSequences = PrepareEntries(response.Docs, "sprite-sequences"),
				Lua = PrepareEntries(response.Docs, "lua")
			};
		}

		public static IReadOnlyDictionary<string, DocsEntry> PrepareEntries(DocsEntry[] docs, string prefix)
		{
			if (docs.Any(entry => entry == null || entry.Location == null || entry.Title == null))
				throw new InvalidDataException("The documentation search index contains an invalid entry.");

			var entries = docs
				.Where(entry => entry.Location.StartsWith($"{prefix}/#") && entry.Location != $"{prefix}/#{prefix}")
				.DistinctBy(entry => entry.Title)
				.ToDictionary(entry => entry.Title, entry => entry);
			return new ReadOnlyDictionary<string, DocsEntry>(entries);
		}

		private static bool IsRecoverableRefreshFailure(Exception exception) => exception is
			HttpRequestException or ApiException or IOException or JsonException or TimeoutException or OperationCanceledException;
	}
}
