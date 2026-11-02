using Discord;
using Microsoft.Extensions.Options;
using System;

namespace Orabot.Bot.Features.Documentation.Rendering
{
	/// <summary>Formats resolved documentation data as the existing OpenRA embeds without performing I/O.</summary>
	public sealed class DocumentationEmbedRenderer
	{
		private readonly string _iconUrl;

		/// <summary>Uses the same configured OpenRA icon as the previous documentation renderers.</summary>
		public DocumentationEmbedRenderer(IOptions<DocumentationLinksOptions> options) => _iconUrl = options.Value.OpenRaFaviconUrl;

		/// <summary>Preserves family labels and descriptions while bounding externally supplied paragraph text.</summary>
		public Embed CreateEmbed(DocumentationLookupResult result)
		{
			var (label, suffix, fallback) = result.Kind switch
			{
				DocumentationKind.Traits => ("OpenRA Traits page", ", trait ", "This documentation is aimed at modders. It displays all traits with default values and developer commentary."),
				DocumentationKind.Weapons => ("OpenRA Weapons page", ", weapon ", "This documentation is aimed at modders. It displays a template for weapon definitions as well as its contained types (warheads and projectiles) with default values and developer commentary."),
				DocumentationKind.Lua => ("OpenRA Lua API page", ", table ", "This documentation is aimed at scripted map creators."),
				DocumentationKind.SpriteSequences => ("OpenRA Sprite Sequences page", ", ", "This documentation is aimed at modders. It displays all sprite sequence types with their properties and their default values plus developer commentary."),
				_ => throw new ArgumentOutOfRangeException(nameof(result))
			};
			var description = result.Kind == DocumentationKind.Lua ? fallback : result.Description ?? fallback;
			if (description.Length > 4096)
				description = description.Substring(0, 4093) + "...";
			return new EmbedBuilder
			{
				Author = new EmbedAuthorBuilder { Name = label + (result.Name == null ? string.Empty : suffix + result.Name), Url = result.TargetUrl, IconUrl = _iconUrl },
				Title = result.TargetUrl,
				Url = result.TargetUrl,
				Description = description
			}.Build();
		}
	}
}
