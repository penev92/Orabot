using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	/// <summary>An explicit global or single-guild command deployment target.</summary>
	public sealed record ApplicationCommandScope
	{
		private ApplicationCommandScope(ulong? guildId) => GuildId = guildId;

		/// <summary>Gets the global application-command scope.</summary>
		public static ApplicationCommandScope Global { get; } = new((ulong?)null);

		/// <summary>Gets the guild identifier, or null for global commands.</summary>
		public ulong? GuildId { get; }

		/// <summary>Gets whether this is the global command scope.</summary>
		public bool IsGlobal => !GuildId.HasValue;

		/// <summary>Creates a deployment target for one nonzero guild identifier.</summary>
		public static ApplicationCommandScope ForGuild(ulong guildId) => guildId != 0
			? new(guildId) : throw new ArgumentOutOfRangeException(nameof(guildId));
	}

	/// <summary>A remote command identity and its SDK-visible definition.</summary>
	public sealed record RemoteApplicationCommand(ulong Id, ApplicationCommandProperties Definition);

	/// <summary>The proposed operation on a command identified by type and name.</summary>
	public enum ApplicationCommandChangeKind
	{
		Create,
		Update,
		Unchanged,
		Preserve,
		Delete
	}

	/// <summary>A reviewable command change including normalized before/after JSON.</summary>
	public sealed record ApplicationCommandChange(
		ApplicationCommandChangeKind Kind,
		ApplicationCommandType Type,
		string Name,
		ulong? ExistingId,
		string BeforeJson,
		string AfterJson);

	/// <summary>A narrow store boundary for reading and individually upserting/deleting command definitions.</summary>
	public interface IApplicationCommandStore
	{
		/// <summary>Reads complete, unlocalized definitions for the selected scope.</summary>
		Task<IReadOnlyList<RemoteApplicationCommand>> ListAsync(ApplicationCommandScope scope, CancellationToken cancellationToken);

		/// <summary>Creates or replaces a command with the same type/name; does not remove other commands.</summary>
		Task UpsertAsync(ApplicationCommandScope scope, ApplicationCommandProperties definition, CancellationToken cancellationToken);

		/// <summary>Deletes one identity obtained by the most recent read of this scope.</summary>
		Task DeleteAsync(ApplicationCommandScope scope, ulong commandId, CancellationToken cancellationToken);
	}
}
