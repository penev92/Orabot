using System;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Orabot.Bot.Discord;
using Orabot.Bot.Features.Quotes;
using Orabot.Bot.Infrastructure.Configuration;
using RestSharp;
using RestSharp.Serializers.Json;
using System.Net.Http;
using System.Threading;
using Orabot.Bot.Discord.Commands;
using Orabot.Bot.Discord.Interactions;
using Orabot.Bot.Discord.Logging;
using Orabot.Bot.Discord.Messages;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Autocomplete;
using Orabot.Bot.Features.Documentation.Commands;
using Orabot.Bot.Features.Documentation.Rendering;
using Orabot.Bot.Features.General;
using Orabot.Bot.Features.GitHubReferences.Messages;
using Orabot.Bot.Features.Logs;
using Orabot.Bot.Features.Maps;
using Orabot.Bot.Features.Maps.Messages;
using Orabot.Bot.Features.Maps.Rendering;
using Orabot.Bot.Features.Replays;
using Orabot.Bot.Features.Roles;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Orabot.Bot.Infrastructure.Serialization;
using Refit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;

namespace Orabot.Bot.DependencyInjection
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection AddOrabot(this IServiceCollection services, IConfiguration configuration)
		{
			return services
				.AddOrabotOptions(configuration)
				.AddSingleton(provider => new DiscordSocketClient(new DiscordSocketConfig
				{
					GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages |
						GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessages |
						GatewayIntents.DirectMessageReactions | GatewayIntents.MessageContent,
					MessageCacheSize = provider.GetRequiredService<IOptions<DiscordBotOptions>>().Value.MessageCacheSize
				}))
				.AddSingleton<CommandService>()
				.AddSingleton<BaseTypeReader, UriTypeReader>()
				.AddSingleton<BaseTypeReader, DiscordMessageIdentifierTypeReader>()
				.AddDefaultEventHandlerServices()
				.AddDefaultCustomMessageHandlers()
				.AddDefaultSlashCommandHandlers()
				.AddDefaultAutocompletionHandlers()
				.AddSingleton<ModuleBase<SocketCommandContext>, GeneralModule>()
				.AddSingleton<ModuleBase<SocketCommandContext>, QuoteModule>()
				.AddSingleton<ModuleBase<SocketCommandContext>, RoleManagementModule>()
				.AddSingleton<QuotingService>()
				.AddDefaultTransformers()
				.AddSingleton<IRestClient>(_ =>
				{
					var serializationOptions = new JsonSerializerOptions();
					serializationOptions.Converters.Add(new CustomDateTimeConverter());
					return new RestClient(new RestClientOptions(),
						configureSerialization: s => s.UseSystemTextJson(serializationOptions));
				})
				.AddDocumentationWebsiteIntegration()
				.AddResourceCenterIntegration()
				.AddYamlDeserializer()
				.AddSingleton(TimeProvider.System)
				.AddSingleton<WorkerFailureState>()
				.AddSingleton<DiscordReadiness>()
				// Reverse stop order lets polling workers finish before the connection is closed.
				.AddHostedService<global::Orabot.Bot.Discord.Bot>()
				.AddLongRunningServices();
		}

		public static IServiceCollection AddOrabotOptions(this IServiceCollection services, IConfiguration configuration)
		{
			services.AddOptions<DiscordBotOptions>()
				.Configure(options =>
				{
					options.BotToken = configuration["BotToken"];
					options.MessageCacheSize = ReadInt<DiscordBotOptions>(configuration, "MessageCacheSize", options.MessageCacheSize);
				})
				.Validate(options => !string.IsNullOrWhiteSpace(options.BotToken), "BotToken must be configured.")
				.Validate(options => options.MessageCacheSize >= 0 && options.MessageCacheSize <= 10000,
					"MessageCacheSize must be between 0 and 10000.")
				.ValidateOnStart();
			services.AddOptions<DocumentationCacheOptions>()
				.Configure(options => options.ScanInterval = ReadInt<DocumentationCacheOptions>(configuration,
					"DocsSearchIndex:ScanInterval", options.ScanInterval))
				.Validate(options => options.ScanInterval > 0, "DocsSearchIndex:ScanInterval must be positive milliseconds.")
				.ValidateOnStart();
			services.AddOptions<MapAnnouncerOptions>()
				.Configure(options =>
				{
					options.ServerName = configuration["MapAnnouncer:ServerName"];
					options.ChannelName = configuration["MapAnnouncer:ChannelName"];
					options.ScanInterval = ReadInt<MapAnnouncerOptions>(configuration, "MapAnnouncer:ScanInterval", options.ScanInterval);
				})
				.Validate(options => !string.IsNullOrWhiteSpace(options.ServerName), "MapAnnouncer:ServerName must be configured.")
				.Validate(options => !string.IsNullOrWhiteSpace(options.ChannelName), "MapAnnouncer:ChannelName must be configured.")
				.Validate(options => options.ScanInterval > 0, "MapAnnouncer:ScanInterval must be positive milliseconds.")
				.ValidateOnStart();
			services.AddSingleton<IValidateOptions<LegacyFeatureOptions>, LegacyFeatureOptionsValidator>();
			services.AddOptions<LegacyFeatureOptions>().Bind(configuration).ValidateOnStart();
			return services;
		}

		public static IServiceCollection AddDefaultEventHandlerServices(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<ILogEventHandler, LogEventHandler>()
				.AddSingleton<IMessageEventHandler, MessageEventHandler>()
				.AddSingleton<IReactionEventHandler, RoleAssignmentReactionEventHandler>()
				.AddSingleton<ISlashCommandEventHandler, SlashCommandEventHandler>()
				.AddSingleton<IAutocompleteEventHandler, AutocompleteEventHandler>();
		}

		public static IServiceCollection AddDefaultCustomMessageHandlers(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<ICustomMessageHandler, OpenRaGitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaModSdkGitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaRa2GitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaResourceCenterGitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaWebGitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OrabotGitHubIssueNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaResourceCenterMapNumberMessageHandler>()
				.AddSingleton<ICustomMessageHandler, OpenRaResourceCenterMapLinkMessageHandler>()
				.AddSingleton<ICustomMessageHandler, LogFileAttachmentMessageHandler>()
				.AddSingleton<ICustomMessageHandler, ReplayFileAttachmentMessageHandler>()
				.AddSingleton<ICustomMessageHandler, BaseCommandMessageHandler>()
				.AddSingleton<ICustomMessageHandler, BaseModTimerMessageHandler>()
				.AddSingleton<ICustomMessageHandler, StackTraceMessageHandler>();
		}

		public static IServiceCollection AddDefaultSlashCommandHandlers(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<ISlashCommandHandler, OpenRaInformationCommandHandler>()
				.AddSingleton<ISlashCommandHandler, LuaDocumentationCommandHandler>()
				.AddSingleton<ISlashCommandHandler, TraitDocumentationCommandHandler>()
				.AddSingleton<ISlashCommandHandler, WeaponDocumentationCommandHandler>()
				.AddSingleton<ISlashCommandHandler, SpriteSequenceDocumentationCommandHandler>();
		}

		public static IServiceCollection AddDefaultAutocompletionHandlers(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<IAutocompleteHandler, LuaTableNamesAutocompleteHandler>()
				.AddSingleton<IAutocompleteHandler, SpriteSequenceNamesAutocompleteHandler>()
				.AddSingleton<IAutocompleteHandler, TraitNamesAutocompleteHandler>()
				.AddSingleton<IAutocompleteHandler, WeaponNamesAutocompleteHandler>();
		}

		public static IServiceCollection AddDefaultTransformers(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<AttachmentLogFileToMessageTransformer>()
				.AddSingleton<OpenRaResourceCenterMapLinkToEmbedTransformer>()
				.AddSingleton<AttachmentReplayMetadataTransformer>()
				.AddSingleton<TraitToEmbedTransformer>()
				.AddSingleton<WeaponToEmbedTransformer>()
				.AddSingleton<LuaTableToEmbedTransformer>()
				.AddSingleton<SpriteSequenceToEmbedTransformer>()
				.AddSingleton<ReplayMetadataToEmbedTransformer>();
		}

		public static IServiceCollection AddYamlDeserializer(this IServiceCollection serviceCollection)
		{
			return serviceCollection.AddSingleton(deserializer =>
				new DeserializerBuilder()
					.IgnoreUnmatchedProperties()
					.WithTypeConverter(new DateTimeConverter(DateTimeKind.Utc, CultureInfo.InvariantCulture, false, "yyyy-MM-dd HH-mm-ss"))
					.Build()
			);
		}

		public static IServiceCollection AddLongRunningServices(this IServiceCollection serviceCollection)
		{
			return serviceCollection
				.AddSingleton<DocsCachingService>()
				.AddHostedService(provider => provider.GetRequiredService<DocsCachingService>())
				.AddHostedService<ResourceCenterMapWatcherService>();
		}

		public static IServiceCollection AddDocumentationWebsiteIntegration(this IServiceCollection serviceCollection)
		{
			var serializerOptions = new JsonSerializerOptions();
			var settings = new RefitSettings
			{
				ContentSerializer = new SystemTextJsonContentSerializer(serializerOptions)
			};

			serviceCollection
				.AddRefitClient<IDocsApi>(settings)
				.ConfigureHttpClient(c =>
				{
					c.BaseAddress = new Uri("https://docs.openra.net/en/");
					c.Timeout = TimeSpan.FromSeconds(30);
				})
				.ConfigurePrimaryHttpMessageHandler(CreatePollingHandler)
				.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

			return serviceCollection;
		}

		public static IServiceCollection AddResourceCenterIntegration(this IServiceCollection serviceCollection)
		{
			var serializerOptions = new JsonSerializerOptions();
			serializerOptions.Converters.Add(new CustomDateTimeConverter());

			var settings = new RefitSettings
			{
				ContentSerializer = new SystemTextJsonContentSerializer(serializerOptions)
			};

			serviceCollection
				.AddRefitClient<IMapsApi>(settings)
				.ConfigureHttpClient(c =>
				{
					c.BaseAddress = new Uri("https://resource.openra.net");
					c.Timeout = TimeSpan.FromSeconds(30);
				})
				.ConfigurePrimaryHttpMessageHandler(CreatePollingHandler)
				.SetHandlerLifetime(Timeout.InfiniteTimeSpan);

			return serviceCollection;
		}
		private static int ReadInt<TOptions>(IConfiguration configuration, string key, int defaultValue)
		{
			var value = configuration[key];
			if (value == null)
				return defaultValue;
			if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
				return result;
			throw new OptionsValidationException(Options.DefaultName, typeof(TOptions),
				new[] { key + " must be a 32-bit integer." });
		}

		// Singleton polling services intentionally keep their typed clients. Renew connections
		// within the handler so DNS changes are respected throughout the process lifetime.
		private static HttpMessageHandler CreatePollingHandler() => new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5)
		};

	}
}
