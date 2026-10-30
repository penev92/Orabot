#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Microsoft.Extensions.Hosting;

namespace Orabot.Tests.Interactions
{
	/// <summary>Runs the real interaction framework against SDK interfaces without a gateway login or REST request.</summary>
	public sealed class OfflineInteractionContext : IInteractionContext
	{
		public OfflineInteractionContext(IDiscordClient client, IDiscordInteraction interaction,
			IGuild guild = null, IMessageChannel channel = null)
		{
			Client = client;
			Interaction = interaction;
			Guild = guild;
			Channel = channel;
		}

		public IDiscordClient Client { get; }
		public IGuild Guild { get; }
		public IMessageChannel Channel { get; }
		public IUser User => Interaction.User;
		public IDiscordInteraction Interaction { get; }
	}

	/// <summary>Records interaction acknowledgments and response edits, with controllable asynchronous transport behavior.</summary>
	public abstract class OfflineDiscordInteraction : IDiscordInteraction
	{
		private static long _sequence;
		private readonly TaskCompletionSource _initialResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<MessageProperties> _firstEdit = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _initialState;
		private int _deferCount;
		private int _respondCount;
		private int _modifyCount;

		protected OfflineDiscordInteraction()
		{
			CreatedAt = DateTimeOffset.UtcNow;
			Id = SnowflakeUtils.ToSnowflake(CreatedAt) + (ulong)(Interlocked.Increment(ref _sequence) & 0x3fffff);
		}

		public ulong Id { get; set; }
		public DateTimeOffset CreatedAt { get; set; }
		public abstract InteractionType Type { get; }
		protected abstract IDiscordInteractionData InteractionData { get; }
		IDiscordInteractionData IDiscordInteraction.Data => InteractionData;
		public string Token => "synthetic-interaction-token";
		public int Version => 1;
		public bool HasResponded => Volatile.Read(ref _initialState) == 2;
		public IUser User { get; set; }
		public string UserLocale => "en-US";
		public string GuildLocale => GuildId.HasValue ? "en-US" : null;
		public bool IsDMInteraction => !GuildId.HasValue;
		public ulong? ChannelId { get; set; } = 200;
		public ulong? GuildId { get; set; }
		public ulong ApplicationId => 100;
		public IReadOnlyCollection<IEntitlement> Entitlements => Array.Empty<IEntitlement>();
		public IReadOnlyDictionary<ApplicationIntegrationType, ulong> IntegrationOwners { get; } =
			new Dictionary<ApplicationIntegrationType, ulong> { [ApplicationIntegrationType.GuildInstall] = 100 };
		public InteractionContextType? ContextType { get; set; } = InteractionContextType.BotDm;
		public GuildPermissions Permissions => default;
		public ulong AttachmentSizeLimit => 20 * 1024 * 1024;

		public ConcurrentQueue<string> Calls { get; } = new();
		public ConcurrentQueue<MessageProperties> ResponseEdits { get; } = new();
		public ConcurrentQueue<MessageProperties> InitialResponses { get; } = new();
		public ConcurrentQueue<MessageProperties> Followups { get; } = new();
		public int DeferCount => Volatile.Read(ref _deferCount);
		public int RespondCount => Volatile.Read(ref _respondCount);
		public int ModifyCount => Volatile.Read(ref _modifyCount);
		public Task InitialResponseRecorded => _initialResponse.Task;
		public Task<MessageProperties> FirstEdit => _firstEdit.Task;
		public Func<bool, RequestOptions, Task> DeferHandler { get; set; }
		public Func<MessageProperties, RequestOptions, Task> ModifyHandler { get; set; }

		public virtual Task DeferAsync(bool ephemeral = false, RequestOptions options = null)
		{
			Interlocked.Increment(ref _deferCount);
			return RecordInitialResponseAsync(ephemeral ? "defer:ephemeral" : "defer:public", options,
				() => DeferHandler?.Invoke(ephemeral, options) ?? Task.CompletedTask);
		}

		public Task RespondAsync(string text = null, Embed[] embeds = null, bool isTTS = false,
			bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null,
			Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
		{
			Interlocked.Increment(ref _respondCount);
			return RecordInitialResponseAsync(ephemeral ? "respond:ephemeral" : "respond:public", options, () =>
			{
				InitialResponses.Enqueue(CreateProperties(text, embeds, embed, allowedMentions));
				return Task.CompletedTask;
			});
		}

		public async Task<IUserMessage> ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options = null)
		{
			if (!HasResponded)
				throw new InvalidOperationException("The original response must be acknowledged before editing.");
			options?.CancelToken.ThrowIfCancellationRequested();
			Interlocked.Increment(ref _modifyCount);
			Calls.Enqueue("modify");
			var properties = new MessageProperties();
			func(properties);
			if (ModifyHandler != null)
				await ModifyHandler(properties, options);
			options?.CancelToken.ThrowIfCancellationRequested();
			ResponseEdits.Enqueue(properties);
			_firstEdit.TrySetResult(properties);
			return null;
		}

		public Task<IUserMessage> FollowupAsync(string text = null, Embed[] embeds = null, bool isTTS = false,
			bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null,
			Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None)
		{
			if (!HasResponded)
				throw new InvalidOperationException("A follow-up requires an initial response.");
			options?.CancelToken.ThrowIfCancellationRequested();
			Calls.Enqueue(ephemeral ? "followup:ephemeral" : "followup:public");
			Followups.Enqueue(CreateProperties(text, embeds, embed, allowedMentions));
			return Task.FromResult<IUserMessage>(null);
		}

		public Task<IUserMessage> GetOriginalResponseAsync(RequestOptions options = null) =>
			throw new NotSupportedException("The fixture records edits without creating an SDK message object.");

		public Task DeleteOriginalResponseAsync(RequestOptions options = null)
		{
			options?.CancelToken.ThrowIfCancellationRequested();
			Calls.Enqueue("delete");
			return Task.CompletedTask;
		}

		protected async Task RecordInitialResponseAsync(string call, RequestOptions options, Func<Task> send)
		{
			options?.CancelToken.ThrowIfCancellationRequested();
			if (Interlocked.CompareExchange(ref _initialState, 1, 0) != 0)
				throw new InvalidOperationException("An interaction can only have one initial response.");
			try
			{
				Calls.Enqueue(call);
				await send();
				options?.CancelToken.ThrowIfCancellationRequested();
				Volatile.Write(ref _initialState, 2);
				_initialResponse.TrySetResult();
			}
			catch
			{
				Volatile.Write(ref _initialState, 0);
				throw;
			}
		}

		private static MessageProperties CreateProperties(string text, Embed[] embeds, Embed embed, AllowedMentions allowedMentions)
		{
			var properties = new MessageProperties();
			if (text != null) properties.Content = text;
			if (embeds != null) properties.Embeds = embeds;
			else if (embed != null) properties.Embeds = new[] { embed };
			if (allowedMentions != null) properties.AllowedMentions = allowedMentions;
			return properties;
		}

		public Task RespondWithFileAsync(Stream fileStream, string fileName, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task RespondWithFileAsync(string filePath, string fileName, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task RespondWithFileAsync(FileAttachment attachment, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task<IUserMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task<IUserMessage> FollowupWithFileAsync(string filePath, string fileName, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task<IUserMessage> FollowupWithFileAsync(FileAttachment attachment, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task<IUserMessage> FollowupWithFilesAsync(IEnumerable<FileAttachment> attachments, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options, PollProperties poll, MessageFlags flags) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task RespondWithModalAsync(Modal modal, RequestOptions options) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
		public Task RespondWithPremiumRequiredAsync(RequestOptions options) => throw new NotSupportedException("This offline fixture supports text and embed responses only.");
	}

	/// <summary>Supplies slash-command data to the SDK parser while recording the complete acknowledgment lifecycle.</summary>
	public sealed class OfflineSlashCommandInteraction : OfflineDiscordInteraction, ISlashCommandInteraction
	{
		public OfflineSlashCommandInteraction(string commandName, params OfflineCommandOption[] options) =>
			Data = new OfflineSlashCommandData(commandName, options);

		public override InteractionType Type => InteractionType.ApplicationCommand;
		public IApplicationCommandInteractionData Data { get; }
		protected override IDiscordInteractionData InteractionData => Data;
	}

	/// <summary>Provides command name and structured options through the SDK's application-command data contract.</summary>
	public sealed class OfflineSlashCommandData : IApplicationCommandInteractionData
	{
		public OfflineSlashCommandData(string name, params OfflineCommandOption[] options)
		{
			Name = name;
			Options = options.Cast<IApplicationCommandInteractionDataOption>().ToArray();
		}

		public ulong Id => 400;
		public string Name { get; }
		public IReadOnlyCollection<IApplicationCommandInteractionDataOption> Options { get; }
	}

	/// <summary>Represents a user-submitted option, including nested options when testing parser failures.</summary>
	public sealed class OfflineCommandOption : IApplicationCommandInteractionDataOption
	{
		public OfflineCommandOption(string name, object value, ApplicationCommandOptionType type = ApplicationCommandOptionType.String,
			params OfflineCommandOption[] options)
		{
			Name = name;
			Value = value;
			Type = type;
			Options = options.Cast<IApplicationCommandInteractionDataOption>().ToArray();
		}

		public string Name { get; }
		public object Value { get; }
		public ApplicationCommandOptionType Type { get; }
		public IReadOnlyCollection<IApplicationCommandInteractionDataOption> Options { get; }
	}

	/// <summary>Exercises actual SDK autocomplete dispatch and records suggestions without allowing deferred responses.</summary>
	public sealed class OfflineAutocompleteInteraction : OfflineDiscordInteraction, IAutocompleteInteraction
	{
		public OfflineAutocompleteInteraction(string commandName, string value = "", string version = "release", string focusedOption = "name") =>
			Data = new OfflineAutocompleteData(commandName, value, version, focusedOption);

		public override InteractionType Type => InteractionType.ApplicationCommandAutocomplete;
		public IAutocompleteInteractionData Data { get; }
		protected override IDiscordInteractionData InteractionData => Data;
		public IReadOnlyList<AutocompleteResult> Choices { get; private set; } = Array.Empty<AutocompleteResult>();
		private int _autocompleteResponseAttempts;
		public int AutocompleteRespondCount { get; private set; }
		public int AutocompleteResponseAttempts => Volatile.Read(ref _autocompleteResponseAttempts);
		public Func<IReadOnlyList<AutocompleteResult>, RequestOptions, Task> AutocompleteResponseHandler { get; set; }

		public Task RespondAsync(IEnumerable<AutocompleteResult> result, RequestOptions options = null)
		{
			Interlocked.Increment(ref _autocompleteResponseAttempts);
			return RecordInitialResponseAsync("autocomplete", options, async () =>
			{
				var choices = result?.ToArray() ?? Array.Empty<AutocompleteResult>();
				if (choices.Length > 25)
					throw new ArgumentException("Discord autocomplete permits at most 25 choices.");
				if (AutocompleteResponseHandler != null)
					await AutocompleteResponseHandler(choices, options);
				Choices = choices;
				AutocompleteRespondCount++;
			});
		}

		public override Task DeferAsync(bool ephemeral = false, RequestOptions options = null) =>
			throw new NotSupportedException("Autocomplete responses cannot be deferred.");
	}

	/// <summary>Constructs SDK autocomplete options using the pinned SDK's nonpublic value constructor.</summary>
	public sealed class OfflineAutocompleteData : IAutocompleteInteractionData
	{
		private static readonly ConstructorInfo OptionConstructor = typeof(AutocompleteOption).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic, null,
			new[] { typeof(ApplicationCommandOptionType), typeof(string), typeof(object), typeof(bool) }, null)
			?? throw new NotSupportedException("The pinned SDK autocomplete-option constructor changed.");

		public OfflineAutocompleteData(string commandName, string value, string version, string focusedOption)
		{
			CommandName = commandName;
			Current = CreateOption(focusedOption, value, true);
			var options = new List<AutocompleteOption> { Current };
			if (focusedOption != "version" && version != null)
				options.Add(CreateOption("version", version, false));
			Options = options;
		}

		public string CommandName { get; }
		public ulong CommandId => 400;
		public ApplicationCommandType Type => ApplicationCommandType.Slash;
		public ulong Version => 1;
		public AutocompleteOption Current { get; }
		public IReadOnlyCollection<AutocompleteOption> Options { get; }

		private static AutocompleteOption CreateOption(string name, string value, bool focused) =>
			(AutocompleteOption)OptionConstructor.Invoke(new object[] { ApplicationCommandOptionType.String, name, value, focused });
	}

	/// <summary>Provides a cancellable host lifetime for offline module execution and shutdown tests.</summary>
	public sealed class OfflineHostApplicationLifetime : IHostApplicationLifetime, IDisposable
	{
		private readonly CancellationTokenSource _stopping = new();
		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => _stopping.Token;
		public CancellationToken ApplicationStopped => CancellationToken.None;
		public void StopApplication() => _stopping.Cancel();
		public void Dispose() => _stopping.Dispose();
	}
}
