using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Discord;
using Discord.Rest;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	internal readonly record struct CommandKey(ApplicationCommandType Type, string Name);

	internal static class ApplicationCommandSchema
	{
		public static ApplicationCommandType TypeOf(ApplicationCommandProperties definition) => definition switch
		{
			SlashCommandProperties => ApplicationCommandType.Slash,
			UserCommandProperties => ApplicationCommandType.User,
			MessageCommandProperties => ApplicationCommandType.Message,
			_ => throw new NotSupportedException("Unsupported application command type.")
		};

		public static CommandKey KeyOf(ApplicationCommandProperties definition)
		{
			if (!definition.Name.IsSpecified || string.IsNullOrWhiteSpace(definition.Name.Value))
				throw new InvalidOperationException("Application commands require a name.");
			return new(TypeOf(definition), definition.Name.Value);
		}

		public static void EnsureUnique(IEnumerable<ApplicationCommandProperties> definitions)
		{
			if (definitions.GroupBy(KeyOf).Any(x => x.Count() != 1))
				throw new InvalidOperationException("Application command type/name identities must be unique.");
		}

		public static ApplicationCommandProperties Prepare(ApplicationCommandProperties definition, ApplicationCommandScope scope)
		{
			KeyOf(definition);
			if (definition.DefaultMemberPermissions.IsSpecified && definition.DefaultMemberPermissions.Value == 0)
				throw new NotSupportedException("Explicit zero default permissions are ambiguous in the SDK remote model.");
			if (definition.IsDefaultPermission.IsSpecified && !definition.IsDefaultPermission.Value)
				throw new NotSupportedException("Deprecated default-permission metadata is not supported.");
			ApplicationCommandProperties result = definition switch
			{
				SlashCommandProperties slash => CreateSlash(slash.Name.Value, slash.Description.IsSpecified ? slash.Description.Value : "",
					(slash.Options.IsSpecified ? slash.Options.Value : null)?.Select(CloneOption)),
				UserCommandProperties => new UserCommandProperties(),
				MessageCommandProperties => new MessageCommandProperties(),
				_ => throw new NotSupportedException("Unsupported application command type.")
			};
			result.Name = definition.Name;
			result.NameLocalizations = CopyDictionary(definition.NameLocalizations);
			result.DescriptionLocalizations = CopyDictionary(definition.DescriptionLocalizations);
			result.DefaultMemberPermissions = definition.DefaultMemberPermissions;
			result.IsNsfw = definition.IsNsfw.IsSpecified && definition.IsNsfw.Value;
			result.IsDMEnabled = Optional<bool>.Unspecified;
			result.IsDefaultPermission = Optional<bool>.Unspecified;
			if (scope.IsGlobal)
			{
				result.ContextTypes = definition.ContextTypes.IsSpecified
					? new HashSet<InteractionContextType>(definition.ContextTypes.Value)
					: Optional<HashSet<InteractionContextType>>.Unspecified;
				result.IntegrationTypes = definition.IntegrationTypes.IsSpecified
					? new HashSet<ApplicationIntegrationType>(definition.IntegrationTypes.Value)
					: Optional<HashSet<ApplicationIntegrationType>>.Unspecified;
			}
			// Do not send deprecated dm_permission or global-only contexts/integration_types
			// to guild endpoints. Names/options/permissions remain the same schema.
			return result;
		}

		public static ApplicationCommandProperties FromRemote(RestApplicationCommand command)
		{
			ApplicationCommandProperties definition = command.Type switch
			{
				ApplicationCommandType.Slash => CreateSlash(command.Name, command.Description, command.Options.Select(FromRemoteOption)),
				ApplicationCommandType.User => new UserCommandProperties(),
				ApplicationCommandType.Message => new MessageCommandProperties(),
				_ => throw new NotSupportedException("Unsupported remote application command type.")
			};
			definition.Name = command.Name;
			definition.NameLocalizations = CopyDictionary(command.NameLocalizations);
			definition.DescriptionLocalizations = CopyDictionary(command.DescriptionLocalizations);
			// Discord.Net 3.20.2 collapses API null and explicit "0" to RawValue zero.
			// Follow its unspecified normalization, and reject requested explicit zero.
			definition.DefaultMemberPermissions = command.DefaultMemberPermissions.RawValue == 0
				? Optional<GuildPermission>.Unspecified : (GuildPermission)command.DefaultMemberPermissions.RawValue;
			definition.IsNsfw = command.IsNsfw;
			definition.ContextTypes = command.ContextTypes != null ? new HashSet<InteractionContextType>(command.ContextTypes)
				: Optional<HashSet<InteractionContextType>>.Unspecified;
			definition.IntegrationTypes = command.IntegrationTypes != null ? new HashSet<ApplicationIntegrationType>(command.IntegrationTypes)
				: Optional<HashSet<ApplicationIntegrationType>>.Unspecified;
			return definition;
		}

		public static string Canonical(ApplicationCommandProperties definition, ApplicationCommandScope scope)
			=> ToJson(definition, scope).ToJsonString();

		public static JsonObject ToJson(ApplicationCommandProperties definition, ApplicationCommandScope scope)
		{
			var prepared = Prepare(definition, scope);
			var json = new JsonObject
			{
				["type"] = (int)TypeOf(prepared), ["name"] = prepared.Name.Value,
				["name_localizations"] = DictionaryJson(prepared.NameLocalizations),
				["default_member_permissions"] = prepared.DefaultMemberPermissions.IsSpecified
					? ((ulong)prepared.DefaultMemberPermissions.Value).ToString(CultureInfo.InvariantCulture) : null,
				["nsfw"] = prepared.IsNsfw.Value
			};
			if (prepared is SlashCommandProperties slash)
			{
				json["description"] = slash.Description.IsSpecified ? slash.Description.Value : "";
				json["description_localizations"] = DictionaryJson(prepared.DescriptionLocalizations);
				json["options"] = new JsonArray(slash.Options.Value.Select(OptionJson).Cast<JsonNode>().ToArray());
			}
			if (scope.IsGlobal)
			{
				var contexts = prepared.ContextTypes.IsSpecified ? prepared.ContextTypes.Value
					: new HashSet<InteractionContextType>(Enum.GetValues<InteractionContextType>());
				var integrations = prepared.IntegrationTypes.IsSpecified ? prepared.IntegrationTypes.Value
					: new HashSet<ApplicationIntegrationType> { ApplicationIntegrationType.GuildInstall };
				json["contexts"] = EnumArray(contexts);
				json["integration_types"] = EnumArray(integrations);
			}
			return json;
		}

		private static JsonObject OptionJson(ApplicationCommandOptionProperties option)
		{
			var json = new JsonObject
			{
				["type"] = (int)option.Type, ["name"] = option.Name, ["description"] = option.Description,
				["name_localizations"] = DictionaryJson(option.NameLocalizations),
				["description_localizations"] = DictionaryJson(option.DescriptionLocalizations)
			};
			if (option.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup)
				json["options"] = new JsonArray((option.Options ?? new()).Select(OptionJson).Cast<JsonNode>().ToArray());
			else
			{
				json["required"] = option.IsRequired ?? false;
				json["autocomplete"] = option.IsAutocomplete;
				json["choices"] = new JsonArray((option.Choices ?? new()).Select(x => new JsonObject
				{
					["name"] = x.Name, ["value"] = ChoiceValue(x.Value),
					["name_localizations"] = DictionaryJson(x.NameLocalizations)
				}).Cast<JsonNode>().ToArray());
				if (option.ChannelTypes?.Count > 0) json["channel_types"] = EnumArray(option.ChannelTypes);
				if (option.MinValue.HasValue) json["min_value"] = option.MinValue.Value;
				if (option.MaxValue.HasValue) json["max_value"] = option.MaxValue.Value;
				if (option.MinLength.HasValue) json["min_length"] = option.MinLength.Value;
				if (option.MaxLength.HasValue) json["max_length"] = option.MaxLength.Value;
			}
			return json;
		}

		private static JsonNode ChoiceValue(object value)
		{
			var element = JsonSerializer.SerializeToElement(value);
			if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
				return JsonNode.Parse(number.ToString("G29", CultureInfo.InvariantCulture));
			if (element.ValueKind == JsonValueKind.String)
				return JsonValue.Create(element.GetString());
			throw new NotSupportedException("Command choices must contain strings or finite numbers.");
		}

		private static JsonArray EnumArray<T>(IEnumerable<T> values) where T : Enum
			=> new(values.Select(x => Convert.ToInt32(x, CultureInfo.InvariantCulture)).Distinct().OrderBy(x => x)
				.Select(x => (JsonNode)JsonValue.Create(x)).ToArray());

		private static JsonObject DictionaryJson(IEnumerable<KeyValuePair<string, string>> dictionary)
		{
			var json = new JsonObject();
			foreach (var pair in (dictionary ?? Array.Empty<KeyValuePair<string, string>>()).OrderBy(x => x.Key, StringComparer.Ordinal))
				json[pair.Key] = pair.Value;
			return json;
		}

		private static IReadOnlyDictionary<string, string> CopyDictionary(IEnumerable<KeyValuePair<string, string>> dictionary)
			=> (dictionary ?? Array.Empty<KeyValuePair<string, string>>()).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

		private static SlashCommandProperties CreateSlash(string name, string description, IEnumerable<ApplicationCommandOptionProperties> options)
		{
			var result = new SlashCommandBuilder { Name = name, Description = description }.Build();
			result.Options = options?.ToList() ?? new List<ApplicationCommandOptionProperties>();
			return result;
		}

		private static ApplicationCommandOptionProperties CloneOption(ApplicationCommandOptionProperties option) => new()
		{
			Name = option.Name, Description = option.Description, Type = option.Type,
			IsRequired = option.IsRequired, IsAutocomplete = option.IsAutocomplete,
			MinValue = option.MinValue, MaxValue = option.MaxValue, MinLength = option.MinLength, MaxLength = option.MaxLength,
			ChannelTypes = option.ChannelTypes?.ToList(),
			NameLocalizations = CopyDictionary(option.NameLocalizations).ToDictionary(x => x.Key, x => x.Value),
			DescriptionLocalizations = CopyDictionary(option.DescriptionLocalizations).ToDictionary(x => x.Key, x => x.Value),
			Options = option.Options?.Select(CloneOption).ToList(),
			Choices = option.Choices?.Select(x => new ApplicationCommandOptionChoiceProperties
			{
				Name = x.Name, Value = x.Value,
				NameLocalizations = CopyDictionary(x.NameLocalizations).ToDictionary(p => p.Key, p => p.Value)
			}).ToList()
		};

		private static ApplicationCommandOptionProperties FromRemoteOption(RestApplicationCommandOption option) => new()
		{
			Name = option.Name, Description = option.Description, Type = option.Type,
			IsRequired = option.IsRequired, IsAutocomplete = option.IsAutocomplete ?? false,
			MinValue = option.MinValue, MaxValue = option.MaxValue, MinLength = option.MinLength, MaxLength = option.MaxLength,
			ChannelTypes = option.ChannelTypes?.ToList(),
			NameLocalizations = CopyDictionary(option.NameLocalizations).ToDictionary(x => x.Key, x => x.Value),
			DescriptionLocalizations = CopyDictionary(option.DescriptionLocalizations).ToDictionary(x => x.Key, x => x.Value),
			Options = option.Options?.Select(FromRemoteOption).ToList(),
			Choices = option.Choices?.Select(x => new ApplicationCommandOptionChoiceProperties
			{
				Name = x.Name, Value = x.Value,
				NameLocalizations = CopyDictionary(x.NameLocalizations).ToDictionary(p => p.Key, p => p.Value)
			}).ToList()
		};
	}
}
