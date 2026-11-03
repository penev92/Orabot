using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging.Abstractions;
using Orabot.Bot.Discord.BackgroundWork;
using Orabot.Bot.Features.Roles;
using Orabot.Bot.Infrastructure.BackgroundWork;
using Orabot.Tests.Interactions;

namespace Orabot.Tests.Roles;

public sealed class ReactionRoleTests
{
	[Fact]
	public async Task Eligible_reaction_downloads_only_after_admission_and_handles_uncached_member()
	{
		var fixture = new RolesFixture();
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, new WorkerFailureState(), capacity: 4, workerCount: 1);
		var handler = new RoleAssignmentReactionEventHandler(client, fixture.Service(), queue, lifetime, NullLogger<RoleAssignmentReactionEventHandler>.Instance);
		var downloads = 0;
		Task<IMessageChannel> Channel(CancellationToken token) { token.ThrowIfCancellationRequested(); downloads++; return Task.FromResult<IMessageChannel>(fixture.Channel); }
		Assert.False(handler.TryQueue(999, RolesFixture.MemberId, "ok", null!, Channel, true));
		Assert.False(handler.TryQueue(123, RolesFixture.MemberId, "other", null!, Channel, true));
		Assert.Equal(0, downloads);
		Assert.True(handler.TryQueue(123, RolesFixture.MemberId, "ok", null!, Channel, true));
		Assert.Equal(0, downloads);
		await queue.StartAsync(CancellationToken.None);
		await Drain(queue);
		Assert.Equal(1, downloads);
		Assert.Equal(1, fixture.UserDownloads);
		Assert.Equal(new[] { "add:30" }, fixture.Writes);
		Assert.True(handler.TryQueue(123, RolesFixture.MemberId, "ok", fixture.Member, Channel, false));
		await Drain(queue);
		Assert.Equal(1, fixture.UserDownloads);
		Assert.Equal(new[] { "add:30", "remove:30" }, fixture.Writes);
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public async Task A_later_remove_cannot_overtake_an_add_waiting_for_a_channel()
	{
		var fixture = new RolesFixture();
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, new WorkerFailureState(), capacity: 4, workerCount: 3);
		var handler = new RoleAssignmentReactionEventHandler(client, fixture.Service(), queue, lifetime, NullLogger<RoleAssignmentReactionEventHandler>.Instance);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var requests = 0;
		async Task<IMessageChannel> Channel(CancellationToken token)
		{
			if (Interlocked.Increment(ref requests) == 1)
			{
				entered.TrySetResult();
				await release.Task.WaitAsync(token);
			}
			return fixture.Channel;
		}
		Assert.True(handler.TryQueue(123, RolesFixture.MemberId, "ok", fixture.Member, Channel, true));
		await queue.StartAsync(CancellationToken.None);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.True(handler.TryQueue(123, RolesFixture.MemberId, "ok", fixture.Member, Channel, false));
		Assert.True(queue.TryEnqueue(_ => { marker.TrySetResult(); return Task.CompletedTask; }));
		await marker.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Empty(fixture.Writes);
		release.TrySetResult();
		await fixture.RemovalFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Equal(new[] { "add:30", "remove:30" }, fixture.Writes);
		Assert.Empty(fixture.MemberRoles);
	}

	[Fact]
	public void Full_queue_or_stopping_host_rejects_work_before_downloads()
	{
		var fixture = new RolesFixture();
		using var client = new DiscordSocketClient();
		using var lifetime = new OfflineHostApplicationLifetime();
		using var queue = new BotWorkQueue(NullLogger<BotWorkQueue>.Instance, new WorkerFailureState(), capacity: 1, workerCount: 1);
		var handler = new RoleAssignmentReactionEventHandler(client, fixture.Service(), queue, lifetime, NullLogger<RoleAssignmentReactionEventHandler>.Instance);
		Assert.True(queue.TryEnqueue(_ => Task.CompletedTask));
		Task<IMessageChannel> Unexpected(CancellationToken _) => throw new InvalidOperationException("Rejected work must not download.");
		Assert.False(handler.TryQueue(123, RolesFixture.MemberId, "ok", fixture.Member, Unexpected, true));
		lifetime.StopApplication();
		Assert.False(handler.TryQueue(123, RolesFixture.MemberId, "ok", fixture.Member, Unexpected, true));
		Assert.Empty(fixture.Writes);
	}

	static async Task Drain(BotWorkQueue queue)
	{
		var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Assert.True(queue.TryEnqueue(_ => { drained.TrySetResult(); return Task.CompletedTask; }));
		await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}
}
