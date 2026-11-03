using Discord;
using Discord.Interactions;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.DependencyInjection;
using Orabot.Bot.Features.Quotes;
using Orabot.Tests.Interactions;

namespace Orabot.Tests.Quotes;

/// <summary>Exercises structured quote routing through the actual SDK and its original-response transport.</summary>
public sealed class QuoteInteractionTests
{
	[Fact]
	public async Task Message_quote_edits_original_with_requester_attribution_and_no_mentions()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		source.AddMessage(100, "@everyone quoted text");
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddInteractionFramework(client)
			.AddSingleton(fixture.Service).AddSingleton<IHostApplicationLifetime>(lifetime).BuildServiceProvider();
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<QuoteModule>(services);
		var command = new OfflineSlashCommandInteraction("quote", new OfflineCommandOption("message", null!, ApplicationCommandOptionType.SubCommand,
			new OfflineCommandOption("message", "200-100")))
		{ GuildId = 1, ContextType = InteractionContextType.Guild, User = fixture.Requester };
		await command.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, command, fixture.Guild, source.Channel), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		Assert.Equal(1, command.DeferCount);
		Assert.Equal(0, command.RespondCount);
		var edit = Assert.Single(command.ResponseEdits);
		Assert.Equal("Requester:", edit.Content.Value);
		Assert.Single(edit.Embeds.Value);
		Assert.True(edit.AllowedMentions.IsSpecified);
		Assert.Equal(AllowedMentionTypes.None, edit.AllowedMentions.Value.AllowedTypes ?? AllowedMentionTypes.None);
		Assert.True(edit.AllowedMentions.Value.UserIds == null || edit.AllowedMentions.Value.UserIds.Count == 0);
		Assert.True(edit.AllowedMentions.Value.RoleIds == null || edit.AllowedMentions.Value.RoleIds.Count == 0);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Source_argument_or_permission_exception_payload_never_reaches_public_response(bool unauthorized)
	{
		const string privatePayload = "https://private.example/attachment?secret=synthetic-private-payload";
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		source.ReadHandler = (_, _) =>
		{
			if (unauthorized) throw new UnauthorizedAccessException(privatePayload);
			throw new ArgumentException(privatePayload);
		};
		using var client = new DiscordRestClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var services = new ServiceCollection().AddInteractionFramework(client)
			.AddSingleton(fixture.Service).AddSingleton<IHostApplicationLifetime>(lifetime).BuildServiceProvider();
		var interactions = services.GetRequiredService<InteractionService>();
		await interactions.AddModuleAsync<QuoteModule>(services);
		var command = new OfflineSlashCommandInteraction("quote", new OfflineCommandOption("message", null!, ApplicationCommandOptionType.SubCommand,
			new OfflineCommandOption("message", "200-100")))
		{ GuildId = 1, ContextType = InteractionContextType.Guild, User = fixture.Requester };
		await command.DeferAsync();

		var result = await interactions.ExecuteCommandAsync(new OfflineInteractionContext(client, command, fixture.Guild, source.Channel), services);

		Assert.True(result.IsSuccess, result.ErrorReason);
		var edit = Assert.Single(command.ResponseEdits);
		Assert.DoesNotContain(privatePayload, edit.Content.Value);
		Assert.DoesNotContain("private.example", edit.Content.Value);
		Assert.Contains(unauthorized ? "view the source channel" : "must belong to this server", edit.Content.Value);
		Assert.Empty(edit.Embeds.Value);
		Assert.True(edit.AllowedMentions.IsSpecified);
		Assert.Equal(AllowedMentionTypes.None, edit.AllowedMentions.Value.AllowedTypes ?? AllowedMentionTypes.None);
		Assert.True(edit.AllowedMentions.Value.UserIds == null || edit.AllowedMentions.Value.UserIds.Count == 0);
		Assert.True(edit.AllowedMentions.Value.RoleIds == null || edit.AllowedMentions.Value.RoleIds.Count == 0);
		Assert.Equal(1, command.DeferCount);
		Assert.Equal(0, command.RespondCount);
	}
}
