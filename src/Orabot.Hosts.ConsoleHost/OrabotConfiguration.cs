using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Orabot.Hosts.ConsoleHost
{
	internal static class OrabotConfiguration
	{
		internal static void Configure(HostApplicationBuilder builder, string[] args)
		{
			AddSources(builder.Configuration, builder.Environment.ContentRootPath,
				builder.Environment.EnvironmentName, args,
				configuration => configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: false));
		}

		internal static void Validate(IServiceProvider services)
		{
			// Host normally resolves hosted services before validating options. Validate first
			// so legacy constructors cannot fail on invalid raw settings before key-only errors.
			try
			{
				services.GetRequiredService<IStartupValidator>().Validate();
			}
			catch (AggregateException exception) when (exception.Flatten().InnerExceptions.All(error => error is OptionsValidationException))
			{
				var failures = exception.Flatten().InnerExceptions.Cast<OptionsValidationException>()
					.SelectMany(error => error.Failures);
				throw new OptionsValidationException(Options.DefaultName, typeof(OrabotConfiguration), failures);
			}
		}

		// The provider callback keeps precedence testable without reading a real user's store.
		internal static IConfigurationBuilder AddSources(IConfigurationBuilder configuration,
			string contentRoot, string environmentName, string[] args,
			Action<IConfigurationBuilder> addUserSecrets)
		{
			configuration.Sources.Clear();
			configuration.SetBasePath(contentRoot)
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
				.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
			// Keep PR #61's optional provider in every environment for existing installations.
			addUserSecrets(configuration);
			configuration.AddEnvironmentVariables(prefix: "ORABOT_").AddCommandLine(args);
			return configuration;
		}
	}
}
