using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orabot.Bot.Discord;
using Orabot.Bot.Features.Maps.Models;
using Orabot.Bot.Features.Maps.Rendering;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Refit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Maps
{
	internal class ResourceCenterMapWatcherService : SupervisedBackgroundService
	{
		private readonly MapAnnouncerOptions _options;
		private readonly DiscordSocketClient _discordClient;
		private readonly IMapsApi _mapsApi;
		private readonly OpenRaResourceCenterMapLinkToEmbedTransformer _toEmbedTransformer;
		private readonly DiscordReadiness _readiness;
		private readonly ILogger<ResourceCenterMapWatcherService> _logger;
		private readonly TimeProvider _timeProvider;
		private int _lastKnownMapId = -1;

		internal int LastKnownMapId => _lastKnownMapId;

		public ResourceCenterMapWatcherService(
			DiscordSocketClient discordClient,
			IMapsApi mapsApi,
			OpenRaResourceCenterMapLinkToEmbedTransformer toEmbedTransformer,
			IOptions<MapAnnouncerOptions> options,
			DiscordReadiness readiness,
			ILogger<ResourceCenterMapWatcherService> logger,
			TimeProvider timeProvider,
			WorkerFailureState failureState) : base(logger, failureState)
		{
			_discordClient = discordClient;
			_mapsApi = mapsApi;
			_toEmbedTransformer = toEmbedTransformer;
			_options = options.Value;
			_readiness = readiness;
			_logger = logger;
			_timeProvider = timeProvider;
		}

		protected override async Task ExecuteSupervisedAsync(CancellationToken stoppingToken)
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					await PollOnceAsync(stoppingToken);
				}
				catch (Exception exception) when (!stoppingToken.IsCancellationRequested && IsRecoverablePollFailure(exception))
				{
					_logger.LogWarning("Resource Center map poll failed; exception type: {ExceptionType}",
						exception.GetType().FullName);
				}

				await Task.Delay(TimeSpan.FromMilliseconds(_options.ScanInterval), _timeProvider, stoppingToken);
			}
		}

		internal async Task PollOnceAsync(CancellationToken cancellationToken)
		{
			await _readiness.WaitUntilReadyAsync(cancellationToken);
			_logger.LogInformation("Checking Resource Center for new maps");
			var response = await _mapsApi.GetMaps(cancellationToken);
			if (response == null || response.Values.Any(map => map == null))
				throw new InvalidDataException("The map index contains an invalid map collection.");

			var maps = response.Values.ToArray();
			if (maps.Length == 0)
				return;

			if (_lastKnownMapId == -1)
			{
				// Preserve startup behavior: establish a cursor without announcing the existing backlog.
				_lastKnownMapId = maps[0].Id;
				return;
			}

			await HandleNewMaps(maps, cancellationToken);
		}

		private async Task HandleNewMaps(IEnumerable<MapInfo> maps, CancellationToken cancellationToken)
		{
			foreach (var map in SelectAnnouncementCandidates(maps))
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (map.Id <= _lastKnownMapId)
					continue;

				await _readiness.WaitUntilReadyAsync(cancellationToken);
				if (FindAvailableDestination() == null)
				{
					LogUnavailableDestination();
					return;
				}

				var embed = await _toEmbedTransformer.CreateEmbed(map, cancellationToken);
				await _readiness.WaitUntilReadyAsync(cancellationToken);
				// Fresh READY can replace the public guild cache before all guilds are available.
				// Re-resolve after awaited rendering so a reconnect cannot consume this announcement.
				var messageChannel = FindAvailableDestination();
				if (messageChannel == null)
				{
					LogUnavailableDestination();
					return;
				}

				if (embed != null)
				{
					await messageChannel.SendMessageAsync($"**{map.Uploader}** uploaded a map:", embed: embed,
						options: new RequestOptions { CancelToken = cancellationToken });
				}

				_lastKnownMapId = map.Id;
				await Task.Delay(TimeSpan.FromSeconds(1), _timeProvider, cancellationToken);
			}
		}

		internal static IEnumerable<MapInfo> SelectAnnouncementCandidates(IEnumerable<MapInfo> maps) => maps.Take(10).Reverse();

		private ISocketMessageChannel FindAvailableDestination()
		{
			var guild = _discordClient.Guilds.FirstOrDefault(candidate => candidate.Name == _options.ServerName);
			if (guild == null || ((IGuild)guild).Available != true)
				return null;
			return guild.Channels.FirstOrDefault(candidate => candidate.Name == _options.ChannelName) as ISocketMessageChannel;
		}

		private void LogUnavailableDestination() =>
			_logger.LogWarning("Map announcement destination is unavailable; retaining the announcement cursor for the next poll");

		private static bool IsRecoverablePollFailure(Exception exception) => exception is
			HttpRequestException or ApiException or HttpException or IOException or JsonException or TimeoutException or
			OperationCanceledException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException;
	}
}
