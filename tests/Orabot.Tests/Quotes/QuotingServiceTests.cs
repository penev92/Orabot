using Discord;
using Orabot.Bot.Features.Quotes;
using Orabot.Tests.TestDoubles;

namespace Orabot.Tests.Quotes;

/// <summary>Verifies authorization precedes retrieval and all lookups/ranges have finite request budgets.</summary>
public sealed class QuotingServiceTests
{
	[Theory]
	[InlineData(false, true, true, true)]
	[InlineData(true, false, true, true)]
	[InlineData(true, true, false, true)]
	[InlineData(true, true, true, false)]
	public async Task Both_requester_and_bot_need_view_and_history_before_any_message_read(bool userView, bool userHistory, bool botView, bool botHistory)
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		source.AddMessage(100);
		fixture.RequesterPermissions[200] = Permissions(userView, userHistory);
		fixture.BotPermissions[200] = Permissions(botView, botHistory);

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100"));
		Assert.Empty(fixture.Reads);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Private_thread_is_refused_even_with_parent_read_permissions(bool custom)
	{
		var fixture = new QuoteFixtures();
		fixture.RequesterPermissions[200] = QuoteFixtures.ReadAccess;
		fixture.BotPermissions[200] = QuoteFixtures.ReadAccess;
		var reads = 0;
		var thread = DiscordTestProxy.Create<IThreadChannel>((method, _) => method switch
		{
			"get_Id" => 200UL, "get_GuildId" => QuoteFixtures.GuildId,
			"get_Type" => ThreadType.PrivateThread, "get_HasJoined" => false,
			"GetMessageAsync" => CountRead(),
			_ => throw new NotSupportedException(method)
		});
		Task<IMessage> CountRead() { reads++; return Task.FromResult<IMessage>(null!); }

		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => custom
			? fixture.Service.CreateCustomQuoteAsync(fixture.Guild, fixture.Requester, "Custom text", channel: thread)
			: fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", channel: thread));
		Assert.Equal(0, reads);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Public_thread_with_read_permissions_remains_supported(bool custom)
	{
		var fixture = new QuoteFixtures();
		var message = fixture.AddChannel(200).AddMessage(100);
		var thread = DiscordTestProxy.Create<IThreadChannel>((method, _) => method switch
		{
			"get_Id" => 200UL, "get_GuildId" => QuoteFixtures.GuildId,
			"get_Name" => "public-thread", "get_Type" => ThreadType.PublicThread,
			"GetMessageAsync" => Task.FromResult(message),
			_ => throw new NotSupportedException(method)
		});

		var embed = custom
			? await fixture.Service.CreateCustomQuoteAsync(fixture.Guild, fixture.Requester, "Custom text", channel: thread)
			: await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", channel: thread);
		Assert.Contains("in #public-thread", embed.Footer!.Value.Text);
	}

	[Fact]
	public async Task Bare_id_tries_current_channel_before_listing_or_searching_other_channels()
	{
		var fixture = new QuoteFixtures();
		fixture.AddChannel(100);
		var preferred = fixture.AddChannel(200);
		preferred.AddMessage(500);

		var embed = await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "500", currentChannel: preferred.Channel);

		Assert.Equal(new[] { 200UL }, fixture.Reads);
		Assert.Equal(0, fixture.GuildChannelListings);
		Assert.Contains("channels/1/200/500", embed.Description);
	}

	[Fact]
	public async Task Bare_id_search_skips_private_channels_and_does_not_search_current_twice()
	{
		var fixture = new QuoteFixtures();
		var current = fixture.AddChannel(100);
		fixture.AddChannel(200).AddMessage(500, "Private");
		fixture.RequesterPermissions[200] = default;
		fixture.AddChannel(300).AddMessage(500, "Visible");

		var embed = await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "500", currentChannel: current.Channel);

		Assert.Equal(new[] { 100UL, 300UL }, fixture.Reads);
		Assert.Contains("Visible", embed.Description);
		Assert.DoesNotContain("Private", embed.Description);
	}

	[Fact]
	public async Task Bare_id_search_is_capped_at_fifty_distinct_authorized_channels()
	{
		var fixture = new QuoteFixtures();
		for (ulong id = 100; id < 160; id++) fixture.AddChannel(id);

		await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "500"));
		Assert.Equal(50, fixture.Reads.Count);
	}

	[Fact]
	public async Task Same_guild_links_can_target_a_channel_other_than_the_current_channel()
	{
		var fixture = new QuoteFixtures();
		var current = fixture.AddChannel(100);
		fixture.AddChannel(200).AddMessage(500);
		var embed = await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester,
			"https://discord.com/channels/1/200/500", currentChannel: current.Channel);
		Assert.Equal(new[] { 200UL }, fixture.Reads);
		Assert.Contains("channels/1/200/500", embed.Description);
	}

	[Fact]
	public async Task Cross_server_links_are_refused_before_retrieval()
	{
		var fixture = new QuoteFixtures();
		var exception = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateMessageQuoteAsync(fixture.Guild,
			fixture.Requester, "https://discord.com/channels/999/200/500"));
		Assert.Contains("Cross-server", exception.Message);
		Assert.Empty(fixture.Reads);
	}

	[Fact]
	public async Task Range_keeps_first_author_and_stops_at_the_requested_end()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		source.AddMessage(100, "First", 30);
		source.Following.Add(source.AddMessage(104, "Beyond endpoint", 30));
		source.Following.Add(source.AddMessage(103, "Last", 30));
		source.Following.Add(source.AddMessage(102, "Other author", 31));
		source.Following.Add(source.AddMessage(101, "Middle", 30));

		var embed = await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", "200-103");

		Assert.StartsWith("First\nMiddle\nLast", embed.Description);
		Assert.DoesNotContain("Other author", embed.Description);
		Assert.DoesNotContain("Beyond endpoint", embed.Description);
		Assert.Equal(25, source.RangeLimit);
	}

	[Fact]
	public async Task Range_never_includes_more_than_first_plus_twenty_five_following_messages()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		source.AddMessage(100, "First");
		for (ulong id = 101; id <= 140; id++) source.Following.Add(source.AddMessage(id, "Line" + id));
		var embed = await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", "200-200");
		Assert.Contains("Line125", embed.Description);
		Assert.DoesNotContain("Line126", embed.Description);
		Assert.Equal(25, source.RangeLimit);
	}

	[Fact]
	public async Task Range_endpoint_from_another_channel_is_refused()
	{
		var fixture = new QuoteFixtures();
		fixture.AddChannel(200).AddMessage(100);
		await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", "300-110"));
	}

	[Fact]
	public async Task Permissions_are_rechecked_after_awaited_source_reads()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var message = source.AddMessage(100, "Now private");
		source.ReadHandler = (_, _) => { fixture.RequesterPermissions[200] = default; return Task.FromResult(message); };
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100"));
	}

	[Fact]
	public async Task Cancellation_reaches_an_in_flight_source_request()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken requestToken = default;
		source.ReadHandler = async (_, options) =>
		{
			requestToken = options.CancelToken;
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, options.CancelToken);
			return null!;
		};
		using var cancellation = new CancellationTokenSource();
		var lookup = fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100", cancellationToken: cancellation.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(10)));
		Assert.True(requestToken.IsCancellationRequested);
	}

	[Fact]
	public async Task Custom_quotes_keep_explicit_author_channel_and_free_timestamp_for_trusted_roles()
	{
		var fixture = new QuoteFixtures();
		var source = fixture.AddChannel(200);
		var embed = await fixture.Service.CreateCustomQuoteAsync(fixture.Guild, fixture.Requester,
			"Custom text", "Displayed author", source.Channel, "Yesterday afternoon");
		Assert.Equal("Displayed author", embed.Author!.Value.Name);
		Assert.Equal("Custom text", embed.Description);
		Assert.Equal("Yesterday afternoon, in #source-200", embed.Footer!.Value.Text);
	}

	[Fact]
	public async Task Custom_quotes_require_the_configured_trusted_role_but_source_quotes_do_not()
	{
		var fixture = new QuoteFixtures { Trusted = false };
		fixture.AddChannel(200).AddMessage(100);
		await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.CreateCustomQuoteAsync(fixture.Guild, fixture.Requester, "Custom text"));
		Assert.NotNull(await fixture.Service.CreateMessageQuoteAsync(fixture.Guild, fixture.Requester, "200-100"));
	}

	private static ChannelPermissions Permissions(bool view, bool history) => new(
		(view ? (ulong)GuildPermission.ViewChannel : 0UL) | (history ? (ulong)GuildPermission.ReadMessageHistory : 0UL));
}
