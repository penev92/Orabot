using System;
using System.Collections.Generic;
using System.Linq;
using Discord;
using Discord.Interactions;

namespace Orabot.Bot.Discord.Interactions.Publishing
{
	// The SDK's REST metadata projector is internal. Use its public metadata model
	// rather than binding production code to private methods by reflection.
	internal static class CommandDefinitionProjection
	{
		public static IReadOnlyList<ApplicationCommandProperties> Project(InteractionService service)
		{
			if (service.LocalizationManager != null)
				throw new NotSupportedException("A localization manager requires an explicit metadata localization projection.");
			var definitions = new List<ApplicationCommandProperties>();
			foreach (var module in service.Modules.Where(x => !x.IsSubModule))
				VisitModule(module, definitions);
			return definitions;
		}

		private static void VisitModule(ModuleInfo module, List<ApplicationCommandProperties> definitions)
		{
			if (module.DontAutoRegister)
				return;
			AddContextCommands(module, definitions);
			if (!module.IsSlashGroup)
			{
				definitions.AddRange(module.SlashCommands.Select(ProjectCommand));
				foreach (var child in module.SubModules)
					VisitModule(child, definitions);
				return;
			}

			var grouped = new SlashCommandBuilder
			{
				Name = module.SlashGroupName,
				Description = module.Description
			}.Build();
			grouped.Options = ProjectGroupOptions(module, definitions, false);
#pragma warning disable CS0618 // Read legacy metadata only to reject unsupported values.
			SetCommon(grouped, module.DefaultPermission, module.IsNsfw, module.DefaultMemberPermissions,
				module.ContextTypes, module.IntegrationTypes);
#pragma warning restore CS0618
			definitions.Add(grouped);
		}

		private static List<ApplicationCommandOptionProperties> ProjectGroupOptions(ModuleInfo module,
			List<ApplicationCommandProperties> definitions, bool isSubgroup)
		{
			var options = new List<ApplicationCommandOptionProperties>();
			foreach (var command in module.SlashCommands)
			{
				if (command.IgnoreGroupNames)
					definitions.Add(ProjectCommand(command));
				else
					options.Add(new ApplicationCommandOptionProperties
					{
						Name = command.Name, Description = command.Description,
						Type = ApplicationCommandOptionType.SubCommand,
						Options = command.FlattenedParameters.Select(ProjectParameter).ToList()
					});
			}

			foreach (var child in module.SubModules.Where(x => !x.DontAutoRegister))
			{
				AddContextCommands(child, definitions);
				if (child.IsSlashGroup)
				{
					if (isSubgroup)
						throw new NotSupportedException("Discord commands support one subgroup level only.");
					options.Add(new ApplicationCommandOptionProperties
					{
						Name = child.SlashGroupName, Description = child.Description,
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Options = ProjectGroupOptions(child, definitions, true)
					});
				}
				else
					options.AddRange(ProjectGroupOptions(child, definitions, isSubgroup));
			}
			return options;
		}

		private static ApplicationCommandProperties ProjectCommand(SlashCommandInfo command)
		{
			var definition = new SlashCommandBuilder
			{
				Name = command.Name, Description = command.Description
			}.Build();
			definition.Options = command.FlattenedParameters.Select(ProjectParameter).ToList();
			GuildPermission? permissions = command.DefaultMemberPermissions.HasValue || command.Module.DefaultMemberPermissions.HasValue
				? (command.DefaultMemberPermissions ?? 0) | (command.Module.DefaultMemberPermissions ?? 0) : null;
			SetCommon(definition, command.DefaultPermission, command.IsNsfw, permissions, command.ContextTypes, command.IntegrationTypes);
			return definition;
		}

		private static void AddContextCommands(ModuleInfo module, List<ApplicationCommandProperties> definitions)
		{
			foreach (var command in module.ContextCommands)
			{
				ApplicationCommandProperties definition = command.CommandType switch
				{
					ApplicationCommandType.Message => new MessageCommandProperties(),
					ApplicationCommandType.User => new UserCommandProperties(),
					_ => throw new NotSupportedException("Unsupported application command type.")
				};
				definition.Name = command.Name;
				GuildPermission? permissions = command.DefaultMemberPermissions.HasValue || module.DefaultMemberPermissions.HasValue
					? (command.DefaultMemberPermissions ?? 0) | (module.DefaultMemberPermissions ?? 0) : null;
				SetCommon(definition, command.DefaultPermission, command.IsNsfw, permissions, command.ContextTypes, command.IntegrationTypes);
				definitions.Add(definition);
			}
		}

		private static void SetCommon(ApplicationCommandProperties definition, bool defaultPermission, bool nsfw,
			GuildPermission? permissions, IReadOnlyCollection<InteractionContextType> contexts,
			IReadOnlyCollection<ApplicationIntegrationType> integrations)
		{
			if (!defaultPermission)
				throw new NotSupportedException("Deprecated default-permission metadata is not supported.");
			if (permissions == 0)
				throw new NotSupportedException("Explicit zero default permissions cannot be distinguished from null in the SDK remote model.");
			definition.IsNsfw = nsfw;
			definition.DefaultMemberPermissions = permissions.HasValue ? permissions.Value : Optional<GuildPermission>.Unspecified;
			definition.ContextTypes = contexts != null ? new HashSet<InteractionContextType>(contexts)
				: new HashSet<InteractionContextType> { InteractionContextType.Guild, InteractionContextType.BotDm };
			definition.IntegrationTypes = integrations != null ? new HashSet<ApplicationIntegrationType>(integrations)
				: new HashSet<ApplicationIntegrationType> { ApplicationIntegrationType.GuildInstall };
		}

		private static ApplicationCommandOptionProperties ProjectParameter(SlashCommandParameterInfo parameter)
		{
			if (!parameter.DiscordOptionType.HasValue)
				throw new NotSupportedException("The parameter does not declare a Discord option type.");
			var option = new ApplicationCommandOptionProperties
			{
				Name = parameter.Name, Description = parameter.Description, Type = parameter.DiscordOptionType.Value,
				IsRequired = parameter.IsRequired, IsAutocomplete = parameter.IsAutocomplete,
				MinValue = parameter.MinValue, MaxValue = parameter.MaxValue,
				MinLength = parameter.MinLength, MaxLength = parameter.MaxLength,
				ChannelTypes = parameter.ChannelTypes?.ToList(),
				Choices = parameter.Choices?.Select(x => new ApplicationCommandOptionChoiceProperties { Name = x.Name, Value = x.Value }).ToList()
			};
			parameter.TypeConverter.Write(option, parameter);
			return option;
		}
	}
}
