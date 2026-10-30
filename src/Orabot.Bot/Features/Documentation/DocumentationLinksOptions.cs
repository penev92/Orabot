namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Feature-owned links bound to the existing root-level documentation configuration keys.</summary>
	public sealed class DocumentationLinksOptions
	{
		public string OpenRaFaviconUrl { get; set; }
		public DocumentationPageOptions Traits { get; set; } = new();
		public DocumentationPageOptions Weapons { get; set; } = new();
		public DocumentationPageOptions Lua { get; set; } = new();
		public DocumentationPageOptions SpriteSequences { get; set; } = new();

		/// <summary>Returns the configured page family without accepting a user-controlled URL.</summary>
		public DocumentationPageOptions GetPages(DocumentationKind kind) => kind switch
		{
			DocumentationKind.Traits => Traits,
			DocumentationKind.Weapons => Weapons,
			DocumentationKind.Lua => Lua,
			DocumentationKind.SpriteSequences => SpriteSequences,
			_ => throw new System.ArgumentOutOfRangeException(nameof(kind))
		};
	}

	/// <summary>The release, playtest and development pages for one documentation family.</summary>
	public sealed class DocumentationPageOptions
	{
		public string ReleasePageUrl { get; set; }
		public string PlaytestPageUrl { get; set; }
		public string DevelopmentPageUrl { get; set; }

		/// <summary>Selects a validated documentation version; unsupported values are rejected.</summary>
		public string GetPageUrl(string version) => version switch
		{
			"release" => ReleasePageUrl,
			"playtest" => PlaytestPageUrl,
			"development" => DevelopmentPageUrl,
			_ => throw new System.ArgumentException("Unsupported documentation version.", nameof(version))
		};
	}
}
