using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orabot.Bot.Discord.BackgroundWork;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Bot.Features.Roles
{
	/// <summary>Admits only configured reaction-role events and performs owned, cancellable work off the Gateway.</summary>
	internal sealed class RoleAssignmentReactionEventHandler : IReactionEventHandler
	{
		private readonly DiscordSocketClient _client;
		private readonly RoleAssignmentService _roles;
		private readonly BotWorkQueue _queue;
		private readonly IHostApplicationLifetime _lifetime;
		private readonly ILogger<RoleAssignmentReactionEventHandler> _logger;
		private readonly object _reactionOrder = new();
		private Task _lastReaction = Task.CompletedTask;

		public RoleAssignmentReactionEventHandler(DiscordSocketClient client, RoleAssignmentService roles, BotWorkQueue queue,
			IHostApplicationLifetime lifetime, ILogger<RoleAssignmentReactionEventHandler> logger)
		{
			_client = client; _roles = roles; _queue = queue; _lifetime = lifetime; _logger = logger;
		}

		public Task HandleReactionAddedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction) =>
			HandleAsync(message.Id, channel, reaction, true);

		public Task HandleReactionRemovedAsync(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction) =>
			HandleAsync(message.Id, channel, reaction, false);

		private Task HandleAsync(ulong messageId, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction, bool add)
		{
			TryQueue(messageId, reaction.UserId, reaction.Emote.Name,
				reaction.User.IsSpecified ? reaction.User.Value as IGuildUser : null,
				async token => channel.HasValue ? channel.Value
					: await _client.GetChannelAsync(channel.Id, new RequestOptions { CancelToken = token }) as IMessageChannel, add);
			return Task.CompletedTask;
		}

		internal bool TryQueue(ulong messageId, ulong userId, string emoteName, IGuildUser cachedMember,
			Func<CancellationToken, Task<IMessageChannel>> getChannel, bool add)
		{
			if (_lifetime.ApplicationStopping.IsCancellationRequested || _roles.AssignmentMessageId == 0
				|| messageId != _roles.AssignmentMessageId || !_roles.TryGetReactionRole(emoteName, out var roleName)
				|| cachedMember?.IsBot == true) return false;
			bool accepted;
			lock (_reactionOrder)
			{
				var previous = _lastReaction;
				var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				accepted = _queue.TryEnqueue(async queueToken =>
				{
					using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(queueToken, _lifetime.ApplicationStopping);
					cancellation.CancelAfter(TimeSpan.FromSeconds(10));
					var token = cancellation.Token;
					try
					{
						// Preserve Gateway admission order even when the shared queue has several
						// consumers and an earlier event needs an uncached member/channel fetch.
						await previous.WaitAsync(token);
						token.ThrowIfCancellationRequested();
						var channel = await getChannel(token);
						if (channel is not IGuildChannel guildChannel) return;
						var guild = guildChannel.Guild;
						var role = guild.Roles.FirstOrDefault(x => string.Equals(x.Name, roleName, StringComparison.Ordinal));
						if (role == null) return;
						var member = cachedMember?.GuildId == guild.Id && cachedMember.Id == userId ? cachedMember
							: await guild.GetUserAsync(userId, CacheMode.AllowDownload, new RequestOptions { CancelToken = token });
						if (member?.Id != userId) return;
						await _roles.SetAsync(guild, member, role, add, token);
					}
					catch (OperationCanceledException) when (token.IsCancellationRequested) { }
					catch (Exception exception)
					{
						_logger.LogWarning("Reaction role work failed ({ExceptionType}).", exception.GetType().FullName);
					}
					finally { completed.TrySetResult(); }
				});
				if (accepted) _lastReaction = completed.Task;
			}
			if (!accepted) _logger.LogWarning("Reaction role work was rejected because the bot work queue is full or closed.");
			return accepted;
		}
	}
}
