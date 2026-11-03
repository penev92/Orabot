using System;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.Hosting;

namespace Orabot.Bot.Features.Roles
{
	/// <summary>Offers configured self-assigned roles through a guild-only slash command.</summary>
	[IntegrationType(ApplicationIntegrationType.GuildInstall)]
	[CommandContextType(InteractionContextType.Guild)]
	[RequireContext(ContextType.Guild)]
	public sealed class RoleManagementModule : InteractionModuleBase<IInteractionContext>
	{
		private readonly RoleAssignmentService _roles;
		private readonly IHostApplicationLifetime _lifetime;

		public RoleManagementModule(RoleAssignmentService roles, IHostApplicationLifetime lifetime)
		{
			_roles = roles;
			_lifetime = lifetime;
		}

		[SlashCommand("role", "Adds or removes a configured self-assigned role.")]
		public async Task RoleAsync([Summary("role", "Choose a self-assigned role, or omit it to list allowed roles.")] IRole role = null)
		{
			var token = _lifetime.ApplicationStopping;
			string response;
			if (Context.Guild == null)
				response = "This command is only available in a server.";
			else if (role == null)
				response = ListRoles();
			else
			{
				var member = Context.User as IGuildUser;
				if (member == null || member.GuildId != Context.Guild.Id)
					member = await Context.Guild.GetUserAsync(Context.User.Id, CacheMode.AllowDownload, new RequestOptions { CancelToken = token });
				response = (await _roles.ToggleAsync(Context.Guild, member, role, token)) switch
				{
					RoleAssignmentResult.Added => "Role added. Run /role with the same role to remove it.",
					RoleAssignmentResult.Removed => "Role removed.",
					RoleAssignmentResult.NotAllowed => "That role is not available for self-assignment.",
					RoleAssignmentResult.UnsafeRole => "That role cannot be assigned here.",
					RoleAssignmentResult.InvalidMember => "This command is only available to members of this server.",
					RoleAssignmentResult.CannotManage => "I cannot manage that role. Ask a server administrator to check my permissions and role order.",
					_ => "The role change could not be confirmed. Check your roles before trying again."
				};
			}
			await Context.Interaction.ModifyOriginalResponseAsync(properties =>
			{
				properties.Content = response;
				properties.AllowedMentions = AllowedMentions.None;
			}, new RequestOptions { CancelToken = token });
		}

		private string ListRoles()
		{
			if (_roles.RoleNames.Count == 0) return "No roles are currently available for self-assignment.";
			var message = new StringBuilder("Use /role and choose one of these configured roles: ");
			foreach (var name in _roles.RoleNames)
			{
				var item = "`" + name.Replace('`', '\'') + "`, ";
				if (message.Length + item.Length > 1900) { message.Append("…"); break; }
				message.Append(item);
			}
			return message.ToString().TrimEnd(' ', ',');
		}
	}
}
