using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.Documentation;
using Orabot.Bot.Features.Documentation.Autocomplete;
using Orabot.Bot.Features.Documentation.Commands;
using Orabot.Bot.Features.Documentation.Models;
using Orabot.Bot.Features.Documentation.Rendering;
using Orabot.Tests.Interactions;

namespace Orabot.Tests.Documentation;

/// <summary>Executes real SDK handlers using recorded interaction interfaces rather than live Discord.</summary>
public sealed class DocumentationInteractionTests
{
	[Theory]
	[InlineData("lua")]
	[InlineData("traits")]
	[InlineData("weapons")]
	[InlineData("sprite-sequences")]
	public async Task Command_edits_the_public_deferred_response_once(string commandName)
	{
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = Services(lifetime, client);
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<DocumentationModule>(services);
		var command = new OfflineSlashCommandInteraction(commandName);
		await command.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, command), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.Equal(1, command.DeferCount);
		Assert.Equal(0, command.RespondCount);
		Assert.Equal(1, command.ModifyCount);
		var edit = Assert.Single(command.ResponseEdits);
		Assert.Single(edit.Embeds.Value);
	}

	[Fact]
	public async Task Invalid_version_is_validated_server_side_even_when_ui_choices_are_bypassed()
	{
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = Services(lifetime, client);
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<DocumentationModule>(services);
		var command = new OfflineSlashCommandInteraction("traits", new OfflineCommandOption("version", "invalid"));
		await command.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, command), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.Contains("release, playtest or development", Assert.Single(command.ResponseEdits).Content.Value);
		Assert.Equal(0, command.RespondCount);
	}

	[Fact]
	public async Task Command_execution_awaits_original_response_transport_completion()
	{
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = Services(lifetime, client);
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<DocumentationModule>(services);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var command = new OfflineSlashCommandInteraction("traits")
		{
			ModifyHandler = (_, _) => { entered.TrySetResult(); return release.Task; }
		};
		await command.DeferAsync();
		var execution = interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, command), services);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.False(execution.IsCompleted);
		release.TrySetResult();
		Assert.True((await execution.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
		Assert.Single(command.ResponseEdits);
	}

	[Theory]
	[InlineData("traits", "Trait")]
	[InlineData("weapons", "Weapon")]
	[InlineData("lua", "Lua")]
	[InlineData("sprite-sequences", "Sequence")]
	public async Task Real_sdk_autocomplete_callback_handles_cold_and_warm_family_caches(string command, string prefix)
	{
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = Services(lifetime, client);
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<DocumentationModule>(services);
		var catalog = services.GetRequiredService<DocumentationCatalog>();
		var cold = new OfflineAutocompleteInteraction(command, prefix.ToUpperInvariant(), "release");

		var coldResult = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, cold), services);

		Assert.True(coldResult.IsSuccess, coldResult.ErrorReason);
		Assert.Empty(cold.Choices);
		Assert.Equal(1, cold.AutocompleteResponseAttempts);
		Assert.Equal(1, cold.AutocompleteRespondCount);
		Assert.Equal(0, cold.DeferCount);
		Assert.Equal(0, cold.RespondCount);
		var docs = new ParsedDocs
		{
			Traits = Entries("Trait"), Weapons = Entries("Weapon"),
			Lua = Entries("Lua"), SpriteSequences = Entries("Sequence")
		};
		catalog.Publish(new DocumentationSnapshot(docs, ParsedDocs.Empty, ParsedDocs.Empty));
		var warm = new OfflineAutocompleteInteraction(command, prefix.ToUpperInvariant(), "release");

		var warmResult = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, warm), services);

		Assert.True(warmResult.IsSuccess, warmResult.ErrorReason);
		Assert.Equal(25, warm.Choices.Count);
		Assert.All(warm.Choices, choice =>
		{
			Assert.StartsWith(prefix + "Key", choice.Name);
			Assert.StartsWith(prefix + "Value", Assert.IsType<string>(choice.Value));
		});
		Assert.Equal(1, warm.AutocompleteResponseAttempts);
		Assert.Equal(1, warm.AutocompleteRespondCount);
		Assert.Equal(new[] { "autocomplete" }, warm.Calls);
		Assert.Equal(0, warm.DeferCount);
		Assert.Equal(0, warm.RespondCount);
		Assert.Equal(0, warm.ModifyCount);

		static IReadOnlyDictionary<string, DocsEntry> Entries(string family) => Enumerable.Range(0, 40)
			.ToDictionary(index => family + "Key" + index, index => new DocsEntry { Title = family + "Value" + index });
	}

	private static ServiceProvider Services(OfflineHostApplicationLifetime lifetime, DiscordRestClient client) => new ServiceCollection()
		.AddInteractionFrameworkMetadata(client)
		.AddSingleton<IHostApplicationLifetime>(lifetime)
		.AddSingleton(TimeProvider.System)
		.AddSingleton<DocumentationCatalog>()
		.AddSingleton<ILogger<DocumentationAutocompleteResponder>>(NullLogger<DocumentationAutocompleteResponder>.Instance)
		.AddSingleton<DocumentationAutocompleteResponder>()
		.AddSingleton(DocumentationOptionsTests.Links())
		.AddSingleton<IDocumentationPageClient>(new NoPages())
		.AddSingleton<DocumentationLookupService>()
		.AddSingleton<DocumentationEmbedRenderer>()
		.BuildServiceProvider();

	private sealed class NoPages : IDocumentationPageClient
	{
		public Task<DocumentationPageEntry> FindEntryAsync(Uri page, string name, CancellationToken cancellationToken) =>
			throw new InvalidOperationException("These page-level commands must not fetch HTML.");
	}
}
