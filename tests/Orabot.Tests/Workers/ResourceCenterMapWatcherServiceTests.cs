using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orabot.Bot.Discord;
using Orabot.Bot.Features.Maps;
using Orabot.Bot.Features.Maps.Models;
using Orabot.Bot.Features.Maps.Rendering;
using Orabot.Bot.Infrastructure.BackgroundWork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Orabot.Tests.Workers
{
	public class ResourceCenterMapWatcherServiceTests
	{
		[Fact]
		public async Task First_poll_waits_for_discord_and_initializes_cursor_without_backlog()
		{
			using var fixture = new MapWorkerFixture();
			fixture.Api.Maps = Maps(20, 19, 18);
			var poll = fixture.Worker.PollOnceAsync(CancellationToken.None);

			Assert.False(poll.IsCompleted);
			Assert.Equal(0, fixture.Api.Calls);
			fixture.Readiness.MarkReady();
			await poll.WaitAsync(TimeSpan.FromSeconds(10));

			Assert.Equal(1, fixture.Api.Calls);
			Assert.Equal(20, fixture.Worker.LastKnownMapId);
		}

		[Fact]
		public async Task Cancellation_while_disconnected_performs_no_map_request()
		{
			using var fixture = new MapWorkerFixture();
			using var cancellation = new CancellationTokenSource();
			var poll = fixture.Worker.PollOnceAsync(cancellation.Token);
			cancellation.Cancel();

			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll.WaitAsync(TimeSpan.FromSeconds(10)));
			Assert.Equal(0, fixture.Api.Calls);
			Assert.Equal(-1, fixture.Worker.LastKnownMapId);
		}

		[Fact]
		public async Task Empty_map_index_leaves_initial_cursor_unset()
		{
			using var fixture = new MapWorkerFixture();
			fixture.Readiness.MarkReady();
			await fixture.Worker.PollOnceAsync(CancellationToken.None);
			Assert.Equal(-1, fixture.Worker.LastKnownMapId);
			fixture.Api.Maps = Maps(30);
			await fixture.Worker.PollOnceAsync(CancellationToken.None);
			Assert.Equal(30, fixture.Worker.LastKnownMapId);
		}

		[Fact]
		public void Announcement_selection_preserves_the_ten_newest_maps_in_oldest_first_order()
		{
			var burst = Maps(Enumerable.Range(1, 25).Reverse().ToArray());
			var candidates = ResourceCenterMapWatcherService.SelectAnnouncementCandidates(burst.Values);
			Assert.Equal(Enumerable.Range(16, 10), candidates.Select(map => map.Id));
		}

		[Fact]
		public async Task Unavailable_destination_retains_initialized_cursor_without_rendering_new_maps()
		{
			using var fixture = new MapWorkerFixture();
			fixture.Readiness.MarkReady();
			fixture.Api.Maps = Maps(10);
			await fixture.Worker.PollOnceAsync(CancellationToken.None);
			fixture.Api.Maps = Maps(11, 10);
			fixture.Api.Maps["11"] = new MapInfo
			{
				Id = 11, Title = "New map", Author = "Fixture author", Bounds = "0,0,64,64", GameMod = "ra", Info = "Fixture"
			};

			// There is no configured guild/channel. The real renderer has no HTTP client, so any
			// attempt to render this complete new map would fail instead of silently passing.
			await fixture.Worker.PollOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
			Assert.Equal(2, fixture.Api.Calls);
			Assert.Equal(10, fixture.Worker.LastKnownMapId);
			await fixture.Worker.PollOnceAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
			Assert.Equal(10, fixture.Worker.LastKnownMapId);
		}

		[Fact]
		public async Task A_disconnected_later_poll_waits_before_querying_resource_center()
		{
			using var fixture = new MapWorkerFixture();
			fixture.Readiness.MarkReady();
			fixture.Api.Maps = Maps(10);
			await fixture.Worker.PollOnceAsync(CancellationToken.None);
			fixture.Readiness.MarkDisconnected();
			using var cancellation = new CancellationTokenSource();
			var poll = fixture.Worker.PollOnceAsync(cancellation.Token);

			Assert.False(poll.IsCompleted);
			Assert.Equal(1, fixture.Api.Calls);
			cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
		}

		[Fact]
		public async Task Hosted_worker_can_stop_before_or_during_initial_readiness_wait()
		{
			using var fixture = new MapWorkerFixture();
			await fixture.Worker.StartAsync(CancellationToken.None);
			await fixture.Worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

			Assert.Equal(0, fixture.Api.Calls);
			Assert.True(fixture.Worker.ExecuteTask!.IsCompleted);
			Assert.False(fixture.Worker.ExecuteTask.IsFaulted);
			Assert.False(fixture.Failures.HasFailed);
		}

		private static Dictionary<string, MapInfo> Maps(params int[] ids) =>
			ids.ToDictionary(id => id.ToString(), id => new MapInfo { Id = id });

		private sealed class FakeMapsApi : IMapsApi
		{
			internal int Calls { get; private set; }
			internal Dictionary<string, MapInfo> Maps { get; set; } = new Dictionary<string, MapInfo>();

			public Task<Dictionary<string, MapInfo>> GetMaps(CancellationToken cancellationToken = default)
			{
				cancellationToken.ThrowIfCancellationRequested();
				Calls++;
				return Task.FromResult(Maps);
			}
		}

		private sealed class MapWorkerFixture : IDisposable
		{
			private readonly DiscordSocketClient _client = new DiscordSocketClient();
			internal FakeMapsApi Api { get; } = new FakeMapsApi();
			internal DiscordReadiness Readiness { get; } = new DiscordReadiness();
			internal WorkerTestClock Clock { get; } = new WorkerTestClock();
			internal WorkerFailureState Failures { get; } = new WorkerFailureState();
			internal ResourceCenterMapWatcherService Worker { get; }

			internal MapWorkerFixture()
			{
				// Any attempted HTTP rendering fails immediately; these tests never log in or send to Discord.
				var renderer = new OpenRaResourceCenterMapLinkToEmbedTransformer(null);
				Worker = new ResourceCenterMapWatcherService(_client, Api, renderer,
					Options.Create(new MapAnnouncerOptions { ServerName = "Fixture", ChannelName = "maps", ScanInterval = 60000 }),
					Readiness, NullLogger<ResourceCenterMapWatcherService>.Instance, Clock, Failures);
			}

			public void Dispose()
			{
				Worker.Dispose();
				_client.Dispose();
			}
		}
	}
}
