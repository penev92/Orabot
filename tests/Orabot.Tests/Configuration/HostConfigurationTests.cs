using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Discord;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Maps;
using Orabot.Hosts.ConsoleHost;

namespace Orabot.Tests.Configuration;

public sealed class HostConfigurationTests
{
	[Fact]
	public void Configuration_uses_explicit_content_root_and_provider_precedence()
	{
		using var files = new SettingsFiles();
		var key = "Precedence" + Guid.NewGuid().ToString("N");
		files.Write("appsettings.json", new() { [key] = "json" });
		files.Write("appsettings.Production.json", new() { [key] = "environment-json" });
		var environmentKey = "ORABOT_" + key;
		try
		{
			Environment.SetEnvironmentVariable(environmentKey, "environment-variable");
			using var fromCommandLine = Build(new[] { "--" + key, "command-line" });
			Assert.Equal("command-line", fromCommandLine[key]);
			using var fromEnvironment = Build(Array.Empty<string>());
			Assert.Equal("environment-variable", fromEnvironment[key]);
			Environment.SetEnvironmentVariable(environmentKey, null);
			using var fromSecrets = Build(Array.Empty<string>());
			Assert.Equal("synthetic-secret", fromSecrets[key]);
			using var withoutSecrets = (ConfigurationRoot)OrabotConfiguration.AddSources(new ConfigurationBuilder(), files.Root,
				"Production", Array.Empty<string>(), _ => { }).Build();
			Assert.Equal("environment-json", withoutSecrets[key]);
		}
		finally
		{
			Environment.SetEnvironmentVariable(environmentKey, null);
		}

		ConfigurationRoot Build(string[] args) => (ConfigurationRoot)OrabotConfiguration.AddSources(new ConfigurationBuilder(),
			files.Root, "Production", args,
			configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = "synthetic-secret" })).Build();
	}

	[Fact]
	public void Missing_optional_environment_and_secret_sources_keep_base_settings()
	{
		using var files = new SettingsFiles();
		files.Write("appsettings.json", new() { ["Sample"] = "base" });
		using var configuration = (ConfigurationRoot)OrabotConfiguration.AddSources(new ConfigurationBuilder(), files.Root,
			"Development", Array.Empty<string>(), _ => { }).Build();
		Assert.Equal("base", configuration["Sample"]);
	}

	[Fact]
	public void Base_settings_file_remains_required()
	{
		using var files = new SettingsFiles();
		Assert.Throws<FileNotFoundException>(() => OrabotConfiguration.AddSources(new ConfigurationBuilder(),
			files.Root, "Production", Array.Empty<string>(), _ => { }).Build());
	}

	[Fact]
	public void Invalid_configuration_fails_before_hosted_services_are_constructed()
	{
		var values = ValidValues();
		values["BotToken"] = "";
		values["MapAnnouncer:ScanInterval"] = "0";
		var constructed = false;
		var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
		builder.Configuration.AddInMemoryCollection(values);
		builder.Services.AddOrabotOptions(builder.Configuration);
		builder.Services.AddSingleton<IHostedService>(_ =>
		{
			constructed = true;
			throw new InvalidOperationException("Must not construct before validation.");
		});
		using var host = builder.Build();
		var error = Assert.Throws<OptionsValidationException>(() => OrabotConfiguration.Validate(host.Services));
		Assert.Contains(error.Failures, failure => failure.Contains("BotToken"));
		Assert.Contains(error.Failures, failure => failure.Contains("MapAnnouncer:ScanInterval"));
		Assert.False(constructed);
	}

	[Theory]
	[InlineData("BotToken", " ")]
	[InlineData("MessageCacheSize", "-1")]
	[InlineData("MessageCacheSize", "10001")]
	[InlineData("DocsSearchIndex:ScanInterval", "0")]
	[InlineData("MapAnnouncer:ScanInterval", "-1")]
	[InlineData("MapAnnouncer:ServerName", " ")]
	[InlineData("MapAnnouncer:ChannelName", " ")]
	[InlineData("MessageCacheSize", "private-numeric-value")]
	[InlineData("MessageCacheSize", "2147483648")]
	[InlineData("DocsSearchIndex:ScanInterval", "private-numeric-value")]
	[InlineData("DocsSearchIndex:ScanInterval", "2147483648")]
	[InlineData("MapAnnouncer:ScanInterval", "private-numeric-value")]
	[InlineData("MapAnnouncer:ScanInterval", "2147483648")]
	public void Typed_options_reject_invalid_settings_without_echoing_values(string key, string value)
	{
		var values = ValidValues();
		values[key] = value;
		using var services = OptionsServices(values);
		var error = Assert.Throws<OptionsValidationException>(() => OrabotConfiguration.Validate(services));
		Assert.Contains(error.Failures, failure => failure.Contains(key));
		Assert.DoesNotContain("synthetic-token", error.Message);
		Assert.DoesNotContain("private-numeric-value", error.Message);
	}

	[Fact]
	public void Complete_synthetic_configuration_passes_startup_validation()
	{
		using var services = OptionsServices(ValidValues());
		OrabotConfiguration.Validate(services);
		Assert.Equal(100, services.GetRequiredService<IOptions<DiscordBotOptions>>().Value.MessageCacheSize);
		Assert.Equal(300000, services.GetRequiredService<IOptions<DocumentationCacheOptions>>().Value.ScanInterval);
	}

	[Fact]
	public void Hosting_and_readers_share_one_documentation_worker()
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidValues()).Build();
		using var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration)
			.AddOrabot(configuration).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
		OrabotConfiguration.Validate(services);
		var hosted = services.GetServices<IHostedService>().ToArray();
		Assert.Equal(3, hosted.Length);
		Assert.IsType<global::Orabot.Bot.Discord.Bot>(hosted[0]);
		Assert.Same(services.GetRequiredService<DocsCachingService>(), hosted[1]);
		Assert.IsType<ResourceCenterMapWatcherService>(hosted[2]);
	}

	static ServiceProvider OptionsServices(Dictionary<string, string?> values)
	{
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		return new ServiceCollection().AddOrabotOptions(configuration).BuildServiceProvider();
	}

	static Dictionary<string, string?> ValidValues()
	{
		var values = new Dictionary<string, string?>
		{
			["BotToken"] = "synthetic-token", ["MapAnnouncer:ServerName"] = "Synthetic server",
			["MapAnnouncer:ChannelName"] = "synthetic-channel", ["GitHubIconsBaseUrl"] = "https://example.org/icons/",
			["OpenRaFaviconUrl"] = "https://example.org/favicon.ico", ["TrustedRoles"] = "",
			["RoleAssignmentMessageId"] = "0", ["FreelyAssignedRolesByEmote"] = "", ["FreelyAssignedRoles"] = "",
			["LogStorageFolder"] = "synthetic-data/logs", ["ReplayStorageFolder"] = "synthetic-data/replays"
		};
		foreach (var section in new[] { "Traits", "Weapons", "Lua", "SpriteSequences" })
			foreach (var version in new[] { "ReleasePageUrl", "PlaytestPageUrl", "DevelopmentPageUrl" })
				values[section + ":" + version] = "https://example.org/docs/";
		return values;
	}

	sealed class SettingsFiles : IDisposable
	{
		public string Root { get; } = Path.Combine(Path.GetTempPath(), "Orabot.Tests-" + Guid.NewGuid().ToString("N"));
		readonly List<string> files = new();
		public SettingsFiles() => Directory.CreateDirectory(Root);
		public void Write(string name, Dictionary<string, string> values)
		{
			var file = Path.Combine(Root, name);
			File.WriteAllText(file, JsonSerializer.Serialize(values));
			files.Add(file);
		}
		public void Dispose()
		{
			foreach (var file in files) File.Delete(file);
			Directory.Delete(Root);
		}
	}
}
