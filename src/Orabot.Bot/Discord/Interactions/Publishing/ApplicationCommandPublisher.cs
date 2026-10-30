using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	/// <summary>A reviewed command deployment plan; creating it never writes to Discord.</summary>
	public sealed class ApplicationCommandPlan
	{
		internal ApplicationCommandPlan(ApplicationCommandScope scope, bool prune,
			IReadOnlyList<ApplicationCommandChange> changes, IReadOnlyList<ApplicationCommandProperties> desired,
			string remoteFingerprint, string fingerprint)
		{
			Scope = scope; Prune = prune; Changes = changes;
			Desired = desired; RemoteFingerprint = remoteFingerprint; Fingerprint = fingerprint;
		}

		/// <summary>Gets the explicit target of this plan.</summary>
		public ApplicationCommandScope Scope { get; }
		/// <summary>Gets whether removal of commands outside the local catalog was explicitly requested.</summary>
		public bool Prune { get; }
		/// <summary>Gets changes, unchanged managed commands and preserved remote commands in deterministic order.</summary>
		public IReadOnlyList<ApplicationCommandChange> Changes { get; }
		/// <summary>Gets the digest of scope, desired definitions and the reviewed remote state.</summary>
		public string Fingerprint { get; }
		/// <summary>Gets whether the plan contains any create, update or delete operation.</summary>
		public bool HasChanges => Changes.Any(x => x.Kind is ApplicationCommandChangeKind.Create or ApplicationCommandChangeKind.Update or ApplicationCommandChangeKind.Delete);
		internal IReadOnlyList<ApplicationCommandProperties> Desired { get; }
		internal string RemoteFingerprint { get; }
	}

	/// <summary>Plans explicit command deployments and applies only reviewed individual changes.</summary>
	public sealed class ApplicationCommandPublisher
	{
		private readonly IApplicationCommandStore _store;

		/// <summary>Uses a command store; neither construction nor export authenticates or contacts Discord.</summary>
		public ApplicationCommandPublisher(IApplicationCommandStore store) => _store = store;

		/// <summary>Exports deterministic, scope-specific command JSON without a token or network connection.</summary>
		public static string ExportJson(IEnumerable<ApplicationCommandProperties> definitions, ApplicationCommandScope scope)
		{
			var desired = PrepareDefinitions(definitions, scope);
			var document = new JsonObject
			{
				["scope"] = scope.IsGlobal ? "global" : "guild",
				["guildId"] = scope.GuildId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
				["commands"] = new JsonArray(desired.Select(x => (JsonNode)ApplicationCommandSchema.ToJson(x, scope)).ToArray())
			};
			return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
		}

		/// <summary>Reads remote state and calculates additions/updates, preserving extra commands unless prune is explicit.</summary>
		public async Task<ApplicationCommandPlan> PlanAsync(IEnumerable<ApplicationCommandProperties> definitions,
			ApplicationCommandScope scope, bool prune = false, CancellationToken cancellationToken = default)
		{
			var desired = PrepareDefinitions(definitions, scope);
			try
			{
				var remote = await _store.ListAsync(scope, cancellationToken);
				return CreatePlan(desired, remote, scope, prune);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch (Exception exception)
			{
				throw new InvalidOperationException($"Application-command planning failed with {exception.GetType().FullName}.");
			}
		}

		/// <summary>Rechecks the reviewed remote state, then applies only changed commands without blanket write retries.</summary>
		public async Task ApplyAsync(ApplicationCommandPlan plan, CancellationToken cancellationToken = default)
		{
			try
			{
				var remote = await _store.ListAsync(plan.Scope, cancellationToken);
				var current = CreatePlan(plan.Desired, remote, plan.Scope, plan.Prune);
				if (!current.HasChanges)
					return;
				if (!string.Equals(current.RemoteFingerprint, plan.RemoteFingerprint, StringComparison.Ordinal))
					throw new CommandDeploymentDriftException();

				var desired = plan.Desired.ToDictionary(ApplicationCommandSchema.KeyOf);
				foreach (var change in current.Changes)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (change.Kind is ApplicationCommandChangeKind.Create or ApplicationCommandChangeKind.Update)
						await _store.UpsertAsync(plan.Scope, ApplicationCommandSchema.Prepare(desired[new(change.Type, change.Name)], plan.Scope), cancellationToken);
					else if (change.Kind == ApplicationCommandChangeKind.Delete)
						await _store.DeleteAsync(plan.Scope, change.ExistingId.Value, cancellationToken);
				}
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch (CommandDeploymentDriftException)
			{
				throw new InvalidOperationException("Remote application commands changed after planning. Generate and review a new plan.");
			}
			catch (Exception exception)
			{
				throw new InvalidOperationException($"Application-command apply failed with {exception.GetType().FullName}. Earlier operations may have completed; generate a new plan before retrying.");
			}
		}

		private static ApplicationCommandPlan CreatePlan(IReadOnlyList<ApplicationCommandProperties> desired,
			IReadOnlyList<RemoteApplicationCommand> remote, ApplicationCommandScope scope, bool prune)
		{
			ApplicationCommandSchema.EnsureUnique(remote.Select(x => x.Definition));
			var existing = remote.ToDictionary(x => ApplicationCommandSchema.KeyOf(x.Definition));
			var changes = new List<ApplicationCommandChange>();
			foreach (var definition in desired)
			{
				var key = ApplicationCommandSchema.KeyOf(definition);
				var after = ApplicationCommandSchema.Canonical(definition, scope);
				if (existing.Remove(key, out var previous))
				{
					var before = ApplicationCommandSchema.Canonical(previous.Definition, scope);
					changes.Add(new(before == after ? ApplicationCommandChangeKind.Unchanged : ApplicationCommandChangeKind.Update,
						key.Type, key.Name, previous.Id, before, after));
				}
				else
					changes.Add(new(ApplicationCommandChangeKind.Create, key.Type, key.Name, null, null, after));
			}
			foreach (var extra in existing)
				changes.Add(new(prune ? ApplicationCommandChangeKind.Delete : ApplicationCommandChangeKind.Preserve,
					extra.Key.Type, extra.Key.Name, extra.Value.Id, ApplicationCommandSchema.Canonical(extra.Value.Definition, scope), null));

			var ordered = changes.OrderBy(x => x.Type).ThenBy(x => x.Name, StringComparer.Ordinal).ToArray();
			var remoteFingerprint = Hash(string.Join("\n", remote.OrderBy(x => ApplicationCommandSchema.TypeOf(x.Definition))
				.ThenBy(x => x.Definition.Name.Value, StringComparer.Ordinal)
				.Select(x => x.Id + ":" + ApplicationCommandSchema.Canonical(x.Definition, scope))));
			var fingerprint = Hash((scope.IsGlobal ? "global" : "guild:" + scope.GuildId) + ":" + prune + ":" + remoteFingerprint
				+ "\n" + string.Join("\n", desired.Select(x => ApplicationCommandSchema.Canonical(x, scope))));
			return new(scope, prune, ordered, desired, remoteFingerprint, fingerprint);
		}

		private static IReadOnlyList<ApplicationCommandProperties> PrepareDefinitions(
			IEnumerable<ApplicationCommandProperties> definitions, ApplicationCommandScope scope)
		{
			ArgumentNullException.ThrowIfNull(scope);
			ArgumentNullException.ThrowIfNull(definitions);
			var prepared = definitions.Select(x => ApplicationCommandSchema.Prepare(x, scope))
				.OrderBy(ApplicationCommandSchema.TypeOf).ThenBy(x => x.Name.Value, StringComparer.Ordinal).ToArray();
			ApplicationCommandSchema.EnsureUnique(prepared);
			return prepared;
		}

		private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
		private sealed class CommandDeploymentDriftException : Exception { }
	}
}
