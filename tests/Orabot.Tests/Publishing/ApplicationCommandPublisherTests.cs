using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Orabot.Bot.Discord.Interactions.Publishing;
using Xunit;

namespace Orabot.Tests.Publishing
{
	/// <summary>Verifies deterministic deployment plans, scoped writes, explicit pruning and remote-drift protection against a fake store.</summary>
	public sealed class ApplicationCommandPublisherTests
	{
		[Fact]
		public async Task IdenticalDefinitionNeedsNoWritesDespiteSdkDefaultAndIdFields()
		{
			var desired = Command("alpha");
			var remote = Command("alpha");
			remote.IsNsfw = Optional<bool>.Unspecified;
			remote.Options = Optional<List<ApplicationCommandOptionProperties>>.Unspecified;
			var store = new FakeStore(new RemoteApplicationCommand(987654321, remote));
			var publisher = new ApplicationCommandPublisher(store);
			var plan = await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global);
			Assert.False(plan.HasChanges);
			Assert.Equal(ApplicationCommandChangeKind.Unchanged, Assert.Single(plan.Changes).Kind);
			await publisher.ApplyAsync(plan);
			Assert.Empty(store.Writes);
		}

		[Fact]
		public async Task CreatesAndUpdatesOnlyManagedChangesAndPreservesUnknownNames()
		{
			var store = new FakeStore(new RemoteApplicationCommand(1, Command("changed", "Old description.")), new(2, Command("other")));
			var publisher = new ApplicationCommandPublisher(store);
			var definitions = new[] { Command("created"), Command("changed", "New description.") };
			var plan = await publisher.PlanAsync(definitions, ApplicationCommandScope.Global);
			Assert.Equal(new[] { ApplicationCommandChangeKind.Update, ApplicationCommandChangeKind.Create, ApplicationCommandChangeKind.Preserve }, plan.Changes.Select(x => x.Kind));
			Assert.Equal((ulong)1, plan.Changes[0].ExistingId);
			Assert.Contains("Old description.", plan.Changes[0].BeforeJson);
			Assert.Contains("New description.", plan.Changes[0].AfterJson);
			await publisher.ApplyAsync(plan);
			Assert.Equal(new[] { "upsert:changed", "upsert:created" }, store.Writes);
			Assert.Contains(store.Remote, x => x.Definition.Name.Value == "other");
			Assert.Equal((ulong)1, store.Remote.Single(x => x.Definition.Name.Value == "changed").Id);

			store.Writes.Clear();
			await publisher.ApplyAsync(plan);
			Assert.Empty(store.Writes);
		}

		[Fact]
		public async Task ExplicitPruneShowsAndDeletesOnlyExtraIdentities()
		{
			var store = new FakeStore(new RemoteApplicationCommand(1, Command("managed")), new(2, Command("extra")));
			var publisher = new ApplicationCommandPublisher(store);
			var plan = await publisher.PlanAsync(new[] { Command("managed") }, ApplicationCommandScope.ForGuild(123), prune: true);
			Assert.True(plan.Prune);
			Assert.Equal(ApplicationCommandChangeKind.Delete, plan.Changes.Single(x => x.Name == "extra").Kind);
			await publisher.ApplyAsync(plan);
			Assert.Equal(new[] { "delete:2" }, store.Writes);
			Assert.Equal((ulong)123, store.LastScope!.GuildId);
		}

		[Fact]
		public async Task TypeAndNameTogetherIdentifyCommands()
		{
			var user = new UserCommandProperties { Name = "same", ContextTypes = Contexts(), IntegrationTypes = Integrations() };
			var store = new FakeStore(new RemoteApplicationCommand(1, Command("same")));
			var publisher = new ApplicationCommandPublisher(store);
			var plan = await publisher.PlanAsync(new ApplicationCommandProperties[] { Command("same"), user }, ApplicationCommandScope.Global);
			Assert.Equal(ApplicationCommandChangeKind.Unchanged, plan.Changes.Single(x => x.Type == ApplicationCommandType.Slash).Kind);
			Assert.Equal(ApplicationCommandChangeKind.Create, plan.Changes.Single(x => x.Type == ApplicationCommandType.User).Kind);
		}

		[Fact]
		public async Task GuildPlansIgnoreAndGuildWritesOmitGlobalOnlyFields()
		{
			var remote = Command("alpha");
			var desired = Command("alpha");
			desired.ContextTypes = new HashSet<InteractionContextType> { InteractionContextType.Guild };
			var store = new FakeStore(new RemoteApplicationCommand(1, remote));
			var publisher = new ApplicationCommandPublisher(store);
			var guildPlan = await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.ForGuild(123));
			Assert.False(guildPlan.HasChanges);
			var globalPlan = await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global);
			Assert.True(globalPlan.HasChanges);

			var created = await publisher.PlanAsync(new[] { Command("new") }, ApplicationCommandScope.ForGuild(123));
			await publisher.ApplyAsync(created);
			var write = Assert.Single(store.WrittenDefinitions);
			Assert.False(write.ContextTypes.IsSpecified);
			Assert.False(write.IntegrationTypes.IsSpecified);
			Assert.False(write.IsDMEnabled.IsSpecified);
		}

		[Fact]
		public async Task ContextAndIntegrationSetsAreOrderIndependent()
		{
			var desired = Command("alpha");
			var remote = Command("alpha");
			remote.ContextTypes = new HashSet<InteractionContextType> { InteractionContextType.BotDm, InteractionContextType.Guild };
			var publisher = new ApplicationCommandPublisher(new FakeStore(new RemoteApplicationCommand(1, remote)));
			Assert.False((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
		}

		[Fact]
		public async Task OptionAndChoiceOrderAndBoundsRemainMeaningful()
		{
			var old = Command("alpha");
			old.Options = new List<ApplicationCommandOptionProperties>
			{
				new() { Name = "name", Description = "Name.", Type = ApplicationCommandOptionType.String, MinLength = 1, MaxLength = 20,
					Choices = new() { new() { Name = "one", Value = "1" }, new() { Name = "two", Value = "2" } } },
				new() { Name = "version", Description = "Version.", Type = ApplicationCommandOptionType.String }
			};
			var publisher = new ApplicationCommandPublisher(new FakeStore(new RemoteApplicationCommand(1, old)));
			var changed = (SlashCommandProperties)ApplicationCommandSchema.Prepare(old, ApplicationCommandScope.Global);
			changed.Options.Value[0].MaxLength = 30;
			Assert.True((await publisher.PlanAsync(new[] { changed }, ApplicationCommandScope.Global)).HasChanges);
			changed = (SlashCommandProperties)ApplicationCommandSchema.Prepare(old, ApplicationCommandScope.Global);
			changed.Options.Value.Reverse();
			Assert.True((await publisher.PlanAsync(new[] { changed }, ApplicationCommandScope.Global)).HasChanges);
			changed = (SlashCommandProperties)ApplicationCommandSchema.Prepare(old, ApplicationCommandScope.Global);
			changed.Options.Value[0].Choices.Reverse();
			Assert.True((await publisher.PlanAsync(new[] { changed }, ApplicationCommandScope.Global)).HasChanges);
		}

		[Fact]
		public async Task PermissionsAndAutocompleteChangesAreNotIgnored()
		{
			var remote = Command("alpha");
			var desired = Command("alpha");
			desired.DefaultMemberPermissions = GuildPermission.ManageRoles;
			var publisher = new ApplicationCommandPublisher(new FakeStore(new RemoteApplicationCommand(1, remote)));
			Assert.True((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
			remote.Options = new List<ApplicationCommandOptionProperties> { new() { Name = "name", Description = "Name.", Type = ApplicationCommandOptionType.String } };
			desired = (SlashCommandProperties)ApplicationCommandSchema.Prepare(remote, ApplicationCommandScope.Global);
			desired.Options.Value[0].IsAutocomplete = true;
			Assert.True((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
		}

		[Fact]
		public async Task NumericChoiceClrTypesDoNotCauseUpdates()
		{
			var remote = Command("alpha");
			remote.Options = new List<ApplicationCommandOptionProperties> { new() { Name = "number", Description = "Number.", Type = ApplicationCommandOptionType.Integer,
				Choices = new() { new() { Name = "one", Value = 1L } } } };
			var desired = (SlashCommandProperties)ApplicationCommandSchema.Prepare(remote, ApplicationCommandScope.Global);
			desired.Options.Value[0].Choices[0].Value = 1.0;
			var publisher = new ApplicationCommandPublisher(new FakeStore(new RemoteApplicationCommand(1, remote)));
			Assert.False((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
		}

		[Fact]
		public async Task LocalizationsAreComparedIndependentlyOfDictionaryOrder()
		{
			var remote = Command("alpha");
			remote.NameLocalizations = new Dictionary<string, string> { ["de"] = "eins", ["fr"] = "un" };
			var desired = Command("alpha");
			desired.NameLocalizations = new Dictionary<string, string> { ["fr"] = "un", ["de"] = "eins" };
			var publisher = new ApplicationCommandPublisher(new FakeStore(new RemoteApplicationCommand(1, remote)));
			Assert.False((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
			desired.NameLocalizations = new Dictionary<string, string> { ["de"] = "zwei", ["fr"] = "un" };
			Assert.True((await publisher.PlanAsync(new[] { desired }, ApplicationCommandScope.Global)).HasChanges);
		}

		[Fact]
		public async Task ChangedRemoteStateRejectsAStalePlanBeforeAnyWrite()
		{
			var store = new FakeStore(new RemoteApplicationCommand(1, Command("alpha", "Old.")));
			var publisher = new ApplicationCommandPublisher(store);
			var plan = await publisher.PlanAsync(new[] { Command("alpha", "Desired.") }, ApplicationCommandScope.Global);
			store.Remote[0] = new(1, Command("alpha", "Other editor."));
			var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.ApplyAsync(plan));
			Assert.Contains("changed after planning", failure.Message);
			Assert.Empty(store.Writes);
		}

		[Fact]
		public async Task FailedWritesAreNotRetriedAndRawErrorsAreNotForwarded()
		{
			var store = new FakeStore { WriteFailure = new InvalidOperationException("synthetic-private-api-payload") };
			var publisher = new ApplicationCommandPublisher(store);
			var plan = await publisher.PlanAsync(new[] { Command("alpha") }, ApplicationCommandScope.Global);
			var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.ApplyAsync(plan));
			Assert.Single(store.Writes);
			Assert.Null(failure.InnerException);
			Assert.DoesNotContain("synthetic-private-api-payload", failure.ToString());
		}

		[Fact]
		public async Task DuplicateIdentityAndExplicitZeroPermissionsFailBeforeReadingRemote()
		{
			var store = new FakeStore();
			var publisher = new ApplicationCommandPublisher(store);
			await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PlanAsync(new[] { Command("same"), Command("same") }, ApplicationCommandScope.Global));
			var explicitZero = Command("alpha");
			explicitZero.DefaultMemberPermissions = (GuildPermission)0;
			await Assert.ThrowsAsync<NotSupportedException>(() => publisher.PlanAsync(new[] { explicitZero }, ApplicationCommandScope.Global));
			Assert.Equal(0, store.Reads);
		}

		[Fact]
		public void OfflineExportIsDeterministicScopeSpecificAndContainsNestedDefinitions()
		{
			var grouped = Command("group");
			grouped.Options = new List<ApplicationCommandOptionProperties> { new() { Name = "child", Description = "Child.", Type = ApplicationCommandOptionType.SubCommand,
				Options = new() { new() { Name = "text", Description = "Text.", Type = ApplicationCommandOptionType.String, IsRequired = true } } } };
			var export = ApplicationCommandPublisher.ExportJson(new[] { grouped, Command("alpha") }, ApplicationCommandScope.Global);
			Assert.Equal(export, ApplicationCommandPublisher.ExportJson(new[] { Command("alpha"), grouped }, ApplicationCommandScope.Global));
			using var document = JsonDocument.Parse(export);
			Assert.Equal("alpha", document.RootElement.GetProperty("commands")[0].GetProperty("name").GetString());
			Assert.Equal("text", document.RootElement.GetProperty("commands")[1].GetProperty("options")[0].GetProperty("options")[0].GetProperty("name").GetString());
			var guildExport = ApplicationCommandPublisher.ExportJson(new[] { grouped }, ApplicationCommandScope.ForGuild(123));
			Assert.DoesNotContain("contexts", guildExport);
			Assert.DoesNotContain("integration_types", guildExport);
			Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationCommandScope.ForGuild(0));
		}

		internal static SlashCommandProperties Command(string name, string description = "A description.")
		{
			var result = new SlashCommandBuilder { Name = name, Description = description,
				ContextTypes = Contexts(), IntegrationTypes = Integrations(), IsNsfw = false }.Build();
			result.Options = new List<ApplicationCommandOptionProperties>();
			return result;
		}

		private static HashSet<InteractionContextType> Contexts() => new() { InteractionContextType.Guild, InteractionContextType.BotDm };
		private static HashSet<ApplicationIntegrationType> Integrations() => new() { ApplicationIntegrationType.GuildInstall };

		private sealed class FakeStore : IApplicationCommandStore
		{
			public FakeStore(params RemoteApplicationCommand[] remote) => Remote = remote.ToList();
			public List<RemoteApplicationCommand> Remote { get; }
			public List<string> Writes { get; } = new();
			public List<ApplicationCommandProperties> WrittenDefinitions { get; } = new();
			public ApplicationCommandScope? LastScope { get; private set; }
			public Exception? WriteFailure { get; set; }
			public int Reads { get; private set; }

			public Task<IReadOnlyList<RemoteApplicationCommand>> ListAsync(ApplicationCommandScope scope, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested(); LastScope = scope; Reads++;
				return Task.FromResult<IReadOnlyList<RemoteApplicationCommand>>(Remote.ToArray());
			}

			public Task UpsertAsync(ApplicationCommandScope scope, ApplicationCommandProperties definition, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested(); LastScope = scope;
				Writes.Add("upsert:" + definition.Name.Value);
				if (WriteFailure != null) return Task.FromException(WriteFailure);
				WrittenDefinitions.Add(definition);
				var key = ApplicationCommandSchema.KeyOf(definition);
				var index = Remote.FindIndex(x => ApplicationCommandSchema.KeyOf(x.Definition) == key);
				var record = new RemoteApplicationCommand(index >= 0 ? Remote[index].Id : (ulong)(100 + Remote.Count), definition);
				if (index >= 0) Remote[index] = record; else Remote.Add(record);
				return Task.CompletedTask;
			}

			public Task DeleteAsync(ApplicationCommandScope scope, ulong commandId, CancellationToken cancellationToken)
			{
				cancellationToken.ThrowIfCancellationRequested(); LastScope = scope;
				Writes.Add("delete:" + commandId); Remote.RemoveAll(x => x.Id == commandId);
				return Task.CompletedTask;
			}
		}
	}
}
