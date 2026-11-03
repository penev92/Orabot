using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Orabot.Bot.Features.General
{
	/// <summary>Provides slash-command help, greetings and role-authorized speaking.</summary>
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
	public sealed class GeneralModule : InteractionModuleBase<IInteractionContext>
	{
		readonly InteractionService commands;
		readonly TrustedRoleOptions trusted;
		readonly IHostApplicationLifetime lifetime;

		public GeneralModule(InteractionService commands, IOptions<TrustedRoleOptions> trusted, IHostApplicationLifetime lifetime)
		{
			this.commands = commands;
			this.trusted = trusted.Value;
			this.lifetime = lifetime;
		}

		[SlashCommand("help", "Lists all available commands.")]
		public async Task HelpAsync()
		{
			var lines = commands.SlashCommands.Select(command =>
			{
				var name = command.Module.IsSlashGroup ? command.Module.SlashGroupName + " " + command.Name : command.Name;
				return "/" + name + " — " + command.Description;
			}).Concat(commands.ContextCommands.Select(command => "Message action: " + command.Name)).OrderBy(line => line, StringComparer.Ordinal);
			var blocks = new List<string>();
			var block = new StringBuilder();
			foreach (var line in lines)
			{
				if (block.Length + line.Length + 1 > 1900)
				{
					blocks.Add(block.ToString());
					block.Clear();
				}
				block.AppendLine(line);
			}
			if (block.Length > 0) blocks.Add(block.ToString());
			if (blocks.Count == 0) blocks.Add("No commands are available.");
			await CompleteAsync(blocks[0]);
			foreach (var next in blocks.Skip(1))
				await Context.Interaction.FollowupAsync(next, allowedMentions: AllowedMentions.None,
					options: new RequestOptions { CancelToken = lifetime.ApplicationStopping });
		}

		[SlashCommand("hi", "Says hi.")]
		public Task HiAsync() => CompleteAsync($"Hi, {Context.User.Mention} !");

		[SlashCommand("say", "Repeats what you say.")]
		[CommandContextType(InteractionContextType.Guild)]
		public Task SayAsync([Summary("message", "The text to repeat."), MaxLength(2000)] string message)
		{
			if (!trusted.IsTrusted(Context.User as IGuildUser, Context.Guild))
				return CompleteAsync("No rights!");
			return CompleteAsync(string.IsNullOrEmpty(message) || message.Length > 2000 ? "Enter between 1 and 2000 characters." : message);
		}

		Task CompleteAsync(string content) => Context.Interaction.ModifyOriginalResponseAsync(properties =>
		{
			properties.Content = content;
			properties.AllowedMentions = AllowedMentions.None;
		}, new RequestOptions { CancelToken = lifetime.ApplicationStopping });
	}
}
