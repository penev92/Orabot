using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using Orabot.Bot.Features.Documentation.Autocomplete;
using Orabot.Bot.Features.Documentation.Rendering;
using System;
using System.Threading.Tasks;

namespace Orabot.Bot.Features.Documentation.Commands
{
	/// <summary>Completes publicly deferred documentation interactions through Discord.Net's command framework.</summary>
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
	public sealed class DocumentationModule : InteractionModuleBase<IInteractionContext>
	{
		private readonly Lazy<DocumentationLookupService> _lookup;
		private readonly Lazy<DocumentationEmbedRenderer> _renderer;
		private readonly IHostApplicationLifetime _lifetime;
		private readonly Lazy<DocumentationAutocompleteResponder> _autocomplete;

		/// <summary>Defers feature activation until invocation, keeping SDK prototype discovery free of configuration and I/O.</summary>
		public DocumentationModule(Lazy<DocumentationLookupService> lookup, Lazy<DocumentationEmbedRenderer> renderer,
			IHostApplicationLifetime lifetime, Lazy<DocumentationAutocompleteResponder> autocomplete)
		{
			_lookup = lookup;
			_renderer = renderer;
			_lifetime = lifetime;
			_autocomplete = autocomplete;
		}

		/// <summary>Links to a Lua table or the Lua API page.</summary>
		[SlashCommand("lua", "Provides a link to the OpenRA Lua API documentation page.")]
		public Task LuaAsync(
			[Summary("name", "The name of the Lua table to link to."), Autocomplete] string name = null,
			[Summary("version", "Which documentation version to link to? Defaults to release."), Choice("release", "release"), Choice("playtest", "playtest"), Choice("development", "development")] string version = "release")
			=> CompleteAsync(DocumentationKind.Lua, name, version);

		/// <summary>Links to a trait or the Traits page.</summary>
		[SlashCommand("traits", "Provides a link to the OpenRA Traits documentation page.")]
		public Task TraitsAsync(
			[Summary("name", "The name of the trait to link to."), Autocomplete] string name = null,
			[Summary("version", "Which documentation version to link to? Defaults to release."), Choice("release", "release"), Choice("playtest", "playtest"), Choice("development", "development")] string version = "release")
			=> CompleteAsync(DocumentationKind.Traits, name, version);

		/// <summary>Links to a weapon definition or the Weapons page.</summary>
		[SlashCommand("weapons", "Provides a link to the OpenRA Weapons documentation page.")]
		public Task WeaponsAsync(
			[Summary("name", "The name of the weapon (Warhead or Projectile) to link to."), Autocomplete] string name = null,
			[Summary("version", "Which documentation version to link to? Defaults to release."), Choice("release", "release"), Choice("playtest", "playtest"), Choice("development", "development")] string version = "release")
			=> CompleteAsync(DocumentationKind.Weapons, name, version);

		/// <summary>Links to a sprite sequence type or its documentation page.</summary>
		[SlashCommand("sprite-sequences", "Provides a link to the OpenRA Sprite Sequences documentation page.")]
		public Task SpriteSequencesAsync(
			[Summary("name", "The name of the sprite sequence type to link to."), Autocomplete] string name = null,
			[Summary("version", "Which documentation version to link to? Defaults to release."), Choice("release", "release"), Choice("playtest", "playtest"), Choice("development", "development")] string version = "release")
			=> CompleteAsync(DocumentationKind.SpriteSequences, name, version);

		/// <summary>Answers Lua suggestions once using a bounded initial response.</summary>
		[AutocompleteCommand(parameterName: "name", commandName: "lua")]
		public Task LuaAutocompleteAsync() => CompleteAutocompleteAsync(DocumentationKind.Lua);

		/// <summary>Answers trait suggestions once using a bounded initial response.</summary>
		[AutocompleteCommand(parameterName: "name", commandName: "traits")]
		public Task TraitsAutocompleteAsync() => CompleteAutocompleteAsync(DocumentationKind.Traits);

		/// <summary>Answers weapon suggestions once using a bounded initial response.</summary>
		[AutocompleteCommand(parameterName: "name", commandName: "weapons")]
		public Task WeaponsAutocompleteAsync() => CompleteAutocompleteAsync(DocumentationKind.Weapons);

		/// <summary>Answers sprite-sequence suggestions once using a bounded initial response.</summary>
		[AutocompleteCommand(parameterName: "name", commandName: "sprite-sequences")]
		public Task SpriteSequencesAutocompleteAsync() => CompleteAutocompleteAsync(DocumentationKind.SpriteSequences);

		private Task CompleteAutocompleteAsync(DocumentationKind kind) => _autocomplete.Value.RespondAsync(
			(IAutocompleteInteraction)Context.Interaction, kind, _lifetime.ApplicationStopping);

		private async Task CompleteAsync(DocumentationKind kind, string name, string version)
		{
			var token = _lifetime.ApplicationStopping;
			if (!DocumentationLookupService.IsInputValid(name, version))
			{
				await Context.Interaction.ModifyOriginalResponseAsync(properties =>
					properties.Content = "Choose release, playtest or development and a name of at most 100 characters.",
					new RequestOptions { CancelToken = token });
				return;
			}
			var result = await _lookup.Value.LookupAsync(kind, name, version, token);
			var embed = _renderer.Value.CreateEmbed(result);
			await Context.Interaction.ModifyOriginalResponseAsync(properties =>
			{
				properties.Embeds = new[] { embed };
				properties.AllowedMentions = AllowedMentions.None;
			}, new RequestOptions { CancelToken = token });
		}
	}
}
