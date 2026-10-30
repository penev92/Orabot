using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Models;
using Orabot.Bot.Infrastructure.BackgroundWork;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Orabot.Tests.Workers
{
	public class DocsCachingServiceTests
	{
		[Fact]
		public void Cache_is_safe_to_read_before_first_refresh()
		{
			using var worker = CreateWorker(new FakeDocsApi(), new WorkerTestClock());
			foreach (var docs in new[] { worker.ReleaseDocs, worker.PlaytestDocs, worker.DevelopmentDocs })
			{
				Assert.Empty(docs.Traits);
				Assert.Empty(docs.Weapons);
				Assert.Empty(docs.SpriteSequences);
				Assert.Empty(docs.Lua);
			}
		}

		[Fact]
		public async Task Refresh_publishes_all_versions_as_one_complete_snapshot()
		{
			var clock = new WorkerTestClock();
			using var worker = CreateWorker(new FakeDocsApi(), clock);
			var original = worker.Snapshot;
			var refresh = Task.Run(() => worker.RefreshAsync(CancellationToken.None));

			Assert.Equal(TimeSpan.FromSeconds(2), await clock.WaitForDelayAsync());
			Assert.Same(original, worker.Snapshot);
			clock.Advance(TimeSpan.FromSeconds(2));
			Assert.Equal(TimeSpan.FromSeconds(2), await clock.WaitForDelayAsync());
			Assert.Same(original, worker.Snapshot);
			clock.Advance(TimeSpan.FromSeconds(2));
			await refresh.WaitAsync(TimeSpan.FromSeconds(10));

			var complete = worker.Snapshot;
			Assert.NotSame(original, complete);
			Assert.Contains("release-1", complete.ReleaseDocs.Traits.Keys);
			Assert.Contains("playtest-1", complete.PlaytestDocs.Traits.Keys);
			Assert.Contains("bleed-1", complete.DevelopmentDocs.Traits.Keys);
		}

		[Fact]
		public async Task Failed_refresh_keeps_the_entire_last_good_snapshot()
		{
			var api = new FakeDocsApi();
			var clock = new WorkerTestClock();
			using var worker = CreateWorker(api, clock);
			await RefreshSuccessfullyAsync(worker, clock);
			var previous = worker.Snapshot;
			api.Generation = 2;
			api.FailingVersion = "playtest";

			var refresh = Task.Run(() => worker.RefreshAsync(CancellationToken.None));
			await clock.AdvanceNextDelayAsync(TimeSpan.FromSeconds(2));
			await Assert.ThrowsAsync<HttpRequestException>(() => refresh.WaitAsync(TimeSpan.FromSeconds(10)));

			Assert.Same(previous, worker.Snapshot);
			Assert.Contains("release-1", worker.ReleaseDocs.Traits.Keys);
			Assert.Contains("playtest-1", worker.PlaytestDocs.Traits.Keys);
			Assert.Contains("bleed-1", worker.DevelopmentDocs.Traits.Keys);
		}

		[Fact]
		public async Task Cancellation_during_refresh_spacing_keeps_last_good_snapshot()
		{
			var api = new FakeDocsApi();
			var clock = new WorkerTestClock();
			using var worker = CreateWorker(api, clock);
			await RefreshSuccessfullyAsync(worker, clock);
			var previous = worker.Snapshot;
			api.Generation = 2;
			using var cancellation = new CancellationTokenSource();

			var refresh = Task.Run(() => worker.RefreshAsync(cancellation.Token));
			Assert.Equal(TimeSpan.FromSeconds(2), await clock.WaitForDelayAsync());
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh.WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.Same(previous, worker.Snapshot);
		}

		[Fact]
		public async Task Stop_cancels_an_in_flight_documentation_request()
		{
			var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			CancellationToken requestToken = default;
			var api = new FakeDocsApi
			{
				Handler = async (_, token) =>
				{
					requestToken = token;
					entered.TrySetResult();
					await Task.Delay(Timeout.InfiniteTimeSpan, token);
					throw new InvalidOperationException("The infinite request fixture completed without cancellation.");
				}
			};
			var failures = new WorkerFailureState();
			using var worker = CreateWorker(api, new WorkerTestClock(), failures);

			await worker.StartAsync(CancellationToken.None);
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

			Assert.True(requestToken.IsCancellationRequested);
			Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
			Assert.False(failures.HasFailed);
		}

		[Fact]
		public async Task Recoverable_outage_keeps_worker_alive_and_shutdown_cancels_scan_delay()
		{
			var clock = new WorkerTestClock();
			var api = new FakeDocsApi { FailingVersion = "release" };
			var failures = new WorkerFailureState();
			using var worker = CreateWorker(api, clock, failures);

			await worker.StartAsync(CancellationToken.None);
			Assert.Equal(TimeSpan.FromMinutes(5), await clock.WaitForDelayAsync());
			Assert.False(worker.ExecuteTask!.IsCompleted);
			Assert.False(failures.HasFailed);
			await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
			Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
		}

		[Fact]
		public async Task Unexpected_worker_failure_is_observable_and_marks_process_failure_state()
		{
			const string privatePayload = "credential-and-payload-fixture";
			var api = new FakeDocsApi
			{
				Handler = (_, _) => throw new InvalidOperationException(privatePayload)
			};
			var failures = new WorkerFailureState();
			var logger = new RecordingLogger();
			using var worker = CreateWorker(api, new WorkerTestClock(), failures, logger);

			await worker.StartAsync(CancellationToken.None);
			var escaped = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.True(failures.HasFailed);
			Assert.Null(escaped.InnerException);
			Assert.DoesNotContain(privatePayload, escaped.Message);
			Assert.Contains(nameof(InvalidOperationException), escaped.Message);
			Assert.All(logger.Entries, entry =>
			{
				Assert.Null(entry.Exception);
				Assert.DoesNotContain(privatePayload, entry.Message);
			});
		}

		[Fact]
		public async Task Recoverable_error_logs_type_without_remote_message_or_exception_payload()
		{
			const string privatePayload = "signed-url-and-response-body-fixture";
			var api = new FakeDocsApi { Handler = (_, _) => throw new HttpRequestException(privatePayload) };
			var clock = new WorkerTestClock();
			var logger = new RecordingLogger();
			using var worker = CreateWorker(api, clock, logger: logger);

			await worker.StartAsync(CancellationToken.None);
			await clock.WaitForDelayAsync();
			await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

			Assert.Contains(logger.Entries, entry => entry.Message.Contains(nameof(HttpRequestException)));
			Assert.All(logger.Entries, entry =>
			{
				Assert.Null(entry.Exception);
				Assert.DoesNotContain(privatePayload, entry.Message);
			});
		}

		private static DocsCachingService CreateWorker(FakeDocsApi api, WorkerTestClock clock,
			WorkerFailureState? failures = null, ILogger<DocsCachingService>? logger = null) =>
			new DocsCachingService(api, Options.Create(new DocumentationCacheOptions { ScanInterval = 300000 }),
				logger ?? NullLogger<DocsCachingService>.Instance, clock, failures ?? new WorkerFailureState());

		private static async Task RefreshSuccessfullyAsync(DocsCachingService worker, WorkerTestClock clock)
		{
			var refresh = Task.Run(() => worker.RefreshAsync(CancellationToken.None));
			await clock.AdvanceNextDelayAsync(TimeSpan.FromSeconds(2));
			await clock.AdvanceNextDelayAsync(TimeSpan.FromSeconds(2));
			await refresh.WaitAsync(TimeSpan.FromSeconds(10));
		}

		private sealed class FakeDocsApi : IDocsApi
		{
			internal int Generation { get; set; } = 1;
			internal string? FailingVersion { get; set; }
			internal Func<string, CancellationToken, Task<DocsSearchIndex>>? Handler { get; set; }

			public Task<DocsSearchIndex> GetSearchIndex(string version, CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (Handler != null)
					return Handler(version, cancellationToken);
				if (version == FailingVersion)
					throw new HttpRequestException("Documentation outage fixture");

				return Task.FromResult(new DocsSearchIndex
				{
					Docs = new[] { new DocsEntry { Title = $"{version}-{Generation}", Location = "traits/#fixture", Text = "Fixture" } }
				});
			}
		}

		private sealed class RecordingLogger : ILogger<DocsCachingService>
		{
			internal ConcurrentQueue<(string Message, Exception? Exception)> Entries { get; } = new();

			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => true;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
				Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Enqueue((formatter(state, exception), exception));
		}
	}
}
