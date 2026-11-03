using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Orabot.Bot.DependencyInjection;

namespace Orabot.Tests.Configuration;

/// <summary>Checks feature-owned URL/storage validation without reading secrets or creating storage folders.</summary>
public sealed class FeatureOptionsTests
{
	[Theory]
	[InlineData("GitHubIconsBaseUrl", "relative-private-value")]
	[InlineData("GitHubIconsBaseUrl", "https://private:secret@example.org/icons/")]
	[InlineData("LogStorageFolder", null)]
	[InlineData("LogStorageFolder", " ")]
	[InlineData("ReplayStorageFolder", "private\0path")]
	public void Invalid_feature_values_report_only_the_key(string key, string? value)
	{
		var values = Values();
		values[key] = value;
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		using var services = new ServiceCollection().AddOrabotOptions(configuration).BuildServiceProvider();
		var error = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
		Assert.Contains(key, error.Message);
		Assert.DoesNotContain("private", error.Message);
		Assert.DoesNotContain("secret", error.Message);
	}

	[Fact]
	public void Validation_does_not_create_storage()
	{
		var values = Values();
		var folder = Path.Combine(Path.GetTempPath(), "orabot-validation-" + Guid.NewGuid().ToString("N"));
		values["LogStorageFolder"] = folder;
		values["ReplayStorageFolder"] = folder;
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
		using var services = new ServiceCollection().AddOrabotOptions(configuration).BuildServiceProvider();
		services.GetRequiredService<IStartupValidator>().Validate();
		Assert.False(Directory.Exists(folder));
	}

	static Dictionary<string, string?> Values() => new()
	{
		["BotToken"] = "synthetic-token", ["MapAnnouncer:ServerName"] = "Test", ["MapAnnouncer:ChannelName"] = "test",
		["GitHubIconsBaseUrl"] = "https://example.org/icons/", ["LogStorageFolder"] = "test-logs", ["ReplayStorageFolder"] = "test-replays"
	};
}
