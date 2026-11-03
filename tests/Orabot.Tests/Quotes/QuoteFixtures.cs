using System.Runtime.CompilerServices;
using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.General;
using Orabot.Bot.Features.Quotes;
using Orabot.Tests.TestDoubles;

namespace Orabot.Tests.Quotes;

/// <summary>Builds explicit public-interface Discord fixtures with visible request and permission behavior.</summary>
internal sealed class QuoteFixtures
{
	internal const ulong GuildId = 1;
	internal static readonly ChannelPermissions ReadAccess = new((ulong)(GuildPermission.ViewChannel | GuildPermission.ReadMessageHistory));
	internal Dictionary<ulong, SourceChannel> Channels { get; } = new();
	internal Dictionary<ulong, ChannelPermissions> RequesterPermissions { get; } = new();
	internal Dictionary<ulong, ChannelPermissions> BotPermissions { get; } = new();
	internal List<ulong> Reads { get; } = new();
	internal bool Trusted { get; set; } = true;
	internal int GuildChannelListings { get; private set; }
	internal IGuild Guild { get; }
	internal IGuildUser Requester { get; }
	internal IGuildUser Bot { get; }
	internal QuotingService Service { get; } = new(Options.Create(new TrustedRoleOptions { TrustedRoles = "Trusted" }));

	internal QuoteFixtures()
	{
		var role = DiscordTestProxy.Create<IRole>((method, _) => method switch
		{
			"get_Id" => 7UL, "get_Name" => "Trusted", _ => throw new NotSupportedException(method)
		});
		Guild = DiscordTestProxy.Create<IGuild>((method, args) => method switch
		{
			"get_Id" => GuildId,
			"get_Name" => "Fixture server",
			"get_Roles" => (IReadOnlyCollection<IRole>)new[] { role },
			"GetCurrentUserAsync" => Task.FromResult(Bot),
			"GetUserAsync" => Task.FromResult((ulong)args![0]! == 10 ? Requester : Bot),
			"GetTextChannelAsync" => Task.FromResult(Channels.GetValueOrDefault((ulong)args![0]!)?.Channel!),
			"GetTextChannelsAsync" => ListChannels(),
			_ => throw new NotSupportedException(method)
		});
		Requester = Member(10, "Requester");
		Bot = Member(20, "Orabot");
	}

	internal SourceChannel AddChannel(ulong id)
	{
		var source = new SourceChannel(this, id);
		Channels.Add(id, source);
		return source;
	}

	internal IGuildUser Member(ulong id, string name) => DiscordTestProxy.Create<IGuildUser>((method, args) => method switch
	{
		"get_Id" => id, "get_GuildId" => GuildId, "get_Guild" => Guild,
		"get_Username" => name, "get_Nickname" => name + " nick",
		"get_RoleIds" => (IReadOnlyCollection<ulong>)(Trusted ? new[] { 7UL } : Array.Empty<ulong>()),
		"GetPermissions" => (id == 20 ? BotPermissions : RequesterPermissions).GetValueOrDefault(((IGuildChannel)args![0]!).Id, ReadAccess),
		"GetAvatarUrl" => "https://example.org/avatar.png",
		_ => throw new NotSupportedException(method)
	});

	private Task<IReadOnlyCollection<ITextChannel>> ListChannels()
	{
		GuildChannelListings++;
		return Task.FromResult<IReadOnlyCollection<ITextChannel>>(Channels.Values.Select(source => source.Channel).ToArray());
	}

	internal sealed class SourceChannel
	{
		private readonly QuoteFixtures _fixture;
		internal ulong Id { get; }
		internal ITextChannel Channel { get; }
		internal Dictionary<ulong, IMessage> Messages { get; } = new();
		internal List<IMessage> Following { get; } = new();
		internal Func<ulong, RequestOptions, Task<IMessage>>? ReadHandler { get; set; }
		internal int RangeLimit { get; private set; }

		internal SourceChannel(QuoteFixtures fixture, ulong id)
		{
			_fixture = fixture;
			Id = id;
			Channel = DiscordTestProxy.Create<ITextChannel>((method, args) => method switch
			{
				"get_Id" => id, "get_Name" => "source-" + id,
				"get_GuildId" => GuildId, "get_Guild" => fixture.Guild,
				"GetMessageAsync" => ReadAsync((ulong)args![0]!, (RequestOptions)args[2]!),
				"GetMessagesAsync" => ReadFollowing((int)args![2]!, (RequestOptions)args[4]!),
				_ => throw new NotSupportedException(method)
			});
		}

		internal IMessage AddMessage(ulong id, string content = "Quoted content", ulong authorId = 30, string? imageUrl = null)
		{
			var author = _fixture.Member(authorId, "Author" + authorId);
			IReadOnlyCollection<IAttachment> attachments = imageUrl == null ? Array.Empty<IAttachment>() :
				new[] { DiscordTestProxy.Create<IAttachment>((method, _) => method == "get_Url" ? imageUrl : throw new NotSupportedException(method)) };
			var message = DiscordTestProxy.Create<IMessage>((method, _) => method switch
			{
				"get_Id" => id, "get_Content" => content, "get_Author" => author, "get_Channel" => Channel,
				"get_Timestamp" => new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero),
				"get_Attachments" => attachments,
				_ => throw new NotSupportedException(method)
			});
			Messages.Add(id, message);
			return message;
		}

		private Task<IMessage> ReadAsync(ulong id, RequestOptions options)
		{
			options.CancelToken.ThrowIfCancellationRequested();
			Assert.True(options.CancelToken.CanBeCanceled);
			_fixture.Reads.Add(Id);
			return ReadHandler?.Invoke(id, options) ?? Task.FromResult(Messages.GetValueOrDefault(id)!);
		}

		private IAsyncEnumerable<IReadOnlyCollection<IMessage>> ReadFollowing(int limit, RequestOptions options)
		{
			RangeLimit = limit;
			return OnePage(Following, options.CancelToken);
		}

		private static async IAsyncEnumerable<IReadOnlyCollection<IMessage>> OnePage(IReadOnlyCollection<IMessage> messages,
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			cancellationToken.ThrowIfCancellationRequested();
			yield return messages;
		}
	}
}
