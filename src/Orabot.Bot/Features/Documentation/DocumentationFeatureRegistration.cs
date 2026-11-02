using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Documentation.Autocomplete;
using Orabot.Bot.Features.Documentation.Rendering;
using Refit;
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;

namespace Orabot.Bot.Features.Documentation
{
	/// <summary>Registers documentation configuration, clients, catalog and rendering.</summary>
	public static class DocumentationFeatureRegistration
	{
		/// <summary>Adds documentation services; the host registers the refresh worker in its chosen lifecycle order.</summary>
		public static IServiceCollection AddDocumentationFeature(this IServiceCollection services, IConfiguration configuration)
		{
			services.AddSingleton<IValidateOptions<DocumentationLinksOptions>, DocumentationLinksOptionsValidator>();
			services.AddOptions<DocumentationLinksOptions>().Bind(configuration).ValidateOnStart();
			services.AddOptions<DocumentationCacheOptions>().Configure(options =>
			{
				var value = configuration["DocsSearchIndex:ScanInterval"];
				if (value != null)
				{
					if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
						throw new OptionsValidationException(nameof(DocumentationCacheOptions), typeof(DocumentationCacheOptions), new[] { "DocsSearchIndex:ScanInterval" });
					options.ScanInterval = interval;
				}
			}).Validate(options => options.ScanInterval > 0, "DocsSearchIndex:ScanInterval must be positive milliseconds.").ValidateOnStart();
			services.AddSingleton<DocumentationCatalog>();
			services.AddSingleton<DocumentationLookupService>();
			services.AddSingleton<DocumentationEmbedRenderer>();
			services.AddSingleton<DocumentationAutocompleteResponder>();
			services.AddHttpClient<IDocumentationPageClient, DocumentationPageClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
				.ConfigurePrimaryHttpMessageHandler(CreateHandler).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
			services.AddRefitClient<IDocsApi>().ConfigureHttpClient(client =>
			{
				client.BaseAddress = new Uri("https://docs.openra.net/en/");
				client.Timeout = TimeSpan.FromSeconds(30);
			}).ConfigurePrimaryHttpMessageHandler(CreateHandler).SetHandlerLifetime(Timeout.InfiniteTimeSpan);
			return services;
		}

		private static HttpMessageHandler CreateHandler() => new SocketsHttpHandler
		{
			PooledConnectionLifetime = TimeSpan.FromMinutes(5),
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
			AllowAutoRedirect = false
		};
	}
}
