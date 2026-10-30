using System;
using System.Globalization;
using System.Linq;
using Orabot.Bot.Discord.Interactions.Publishing;

namespace Orabot.Hosts.ConsoleHost.CommandDeployment
{
	internal enum CommandDeploymentOperation { Help, Export, Plan, Apply }

	/// <summary>Parses the explicit deployment mode before ordinary bot configuration or startup runs.</summary>
	internal sealed record CommandDeploymentArguments(CommandDeploymentOperation Operation,
		ApplicationCommandScope Scope, bool Prune, string ExpectedPlan)
	{
		public static bool TryParse(string[] args, out CommandDeploymentArguments request, out string error)
		{
			request = null;
			error = null;
			if (args.Length == 0 || !string.Equals(args[0], "commands", StringComparison.OrdinalIgnoreCase))
				return Fail("Expected the commands subcommand.", out error);
			if (args.Length == 1 || (args.Length == 2 && args[1] is "help" or "--help" or "-h"))
			{
				request = new(CommandDeploymentOperation.Help, ApplicationCommandScope.Global, false, null);
				return true;
			}
			var operation = args[1].ToLowerInvariant() switch
			{
				"export" => CommandDeploymentOperation.Export,
				"plan" => CommandDeploymentOperation.Plan,
				"apply" => CommandDeploymentOperation.Apply,
				_ => CommandDeploymentOperation.Help
			};
			if (operation == CommandDeploymentOperation.Help)
				return Fail("Choose commands export, plan or apply.", out error);
			ApplicationCommandScope scope = null;
			var prune = false;
			string expected = null;
			for (var index = 2; index < args.Length; index++)
			{
				switch (args[index])
				{
					case "--global":
						if (scope != null) return Fail("Choose exactly one target: --global or --guild <id>.", out error);
						scope = ApplicationCommandScope.Global;
						break;
					case "--guild":
						if (scope != null) return Fail("Choose exactly one target: --global or --guild <id>.", out error);
						if (++index >= args.Length || !ulong.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
							return Fail("--guild requires a nonzero Discord guild ID.", out error);
						scope = ApplicationCommandScope.ForGuild(id);
						break;
					case "--prune":
						if (prune || operation == CommandDeploymentOperation.Export)
							return Fail("--prune is available once with plan or apply.", out error);
						prune = true;
						break;
					case "--expected-plan":
						if (operation != CommandDeploymentOperation.Apply || expected != null || ++index >= args.Length
							|| args[index].Length != 64 || !args[index].All(Uri.IsHexDigit))
							return Fail("--expected-plan requires a plan fingerprint and is available once with apply.", out error);
						expected = args[index].ToLowerInvariant();
						break;
					default:
						return Fail("Unknown deployment option. Use commands help.", out error);
				}
			}
			if (scope == null && operation != CommandDeploymentOperation.Export)
				return Fail("Planning and applying require an explicit --guild <id> or --global target.", out error);
			request = new(operation, scope ?? ApplicationCommandScope.Global, prune, expected);
			return true;
		}

		private static bool Fail(string message, out string error) { error = message; return false; }
	}
}
