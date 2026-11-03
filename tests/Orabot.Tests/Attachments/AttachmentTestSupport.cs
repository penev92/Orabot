using Discord;
using Orabot.Bot.Infrastructure.Attachments;
using Orabot.Tests.TestDoubles;
using System.Net;

namespace Orabot.Tests.Attachments;

internal sealed class AttachmentFiles : IDisposable
{
	public string Root { get; } = Path.Combine(Path.GetTempPath(), "Orabot-AttachmentTests-" + Guid.NewGuid().ToString("N"));
	public AttachmentFiles() => Directory.CreateDirectory(Root);
	public void Dispose()
	{
		var path = Path.GetFullPath(Root);
		if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Test cleanup escaped its temporary root.");
		if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
	}
}

internal sealed class AttachmentHttp : IHttpClientFactory, IDisposable
{
	readonly FixtureHandler handler;
	public int Requests { get; private set; }
	public AttachmentHttp(Func<CancellationToken, Task<HttpResponseMessage>> response) => handler = new FixtureHandler(async token =>
	{
		Requests++;
		return await response(token);
	});
	public AttachmentHttp(byte[] body) : this(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
		{ Content = new ByteArrayContent(body) })) { }
	public HttpClient CreateClient(string name)
	{
		Assert.Equal(AttachmentDownloader.ClientName, name);
		return new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
	}
	public void Dispose() => handler.Dispose();

	sealed class FixtureHandler(Func<CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => response(token);
	}
}

internal static class AttachmentFixtures
{
	public static IAttachment Attachment(int size = 0, string filename = "crash.log",
		string url = "https://cdn.discordapp.com/attachments/100/200/crash.log") =>
		DiscordTestProxy.Create<IAttachment>((method, _) => method switch
		{
			"get_Filename" => filename, "get_Url" => url, "get_Size" => size,
			_ => throw new NotSupportedException(method)
		});

	public static IUserMessage Message(IAttachment attachment, string content, Action<string, Embed?, AllowedMentions, RequestOptions> sent)
	{
		var author = DiscordTestProxy.Create<IUser>((method, _) => method switch
		{
			"get_Id" => 123UL, "get_Mention" => "<@123>", _ => throw new NotSupportedException(method)
		});
		var channel = DiscordTestProxy.Create<IMessageChannel>((method, args) =>
		{
			if (method != "SendMessageAsync") throw new NotSupportedException(method);
			sent((string)args![0]!, args.OfType<Embed>().SingleOrDefault(), args.OfType<AllowedMentions>().Single(),
				args.OfType<RequestOptions>().Single());
			return Task.FromResult<IUserMessage>(null!);
		});
		return DiscordTestProxy.Create<IUserMessage>((method, _) => method switch
		{
			"get_Attachments" => attachment == null ? Array.Empty<IAttachment>() : new[] { attachment },
			"get_Content" => content, "get_Author" => author, "get_Channel" => channel,
			_ => throw new NotSupportedException(method)
		});
	}
}

internal sealed class StreamingContent(Stream input) : HttpContent
{
	protected override bool TryComputeLength(out long length) { length = 0; return false; }
	protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
	protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(input);
	protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult(input);
}

internal sealed class CancellableBody(TaskCompletionSource entered) : Stream
{
	bool emitted;
	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => throw new NotSupportedException();
	public override long Position { get => 0; set => throw new NotSupportedException(); }
	public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
	{
		if (!emitted) { emitted = true; buffer.Span[0] = 1; return 1; }
		entered.TrySetResult();
		await Task.Delay(Timeout.InfiniteTimeSpan, token);
		return 0;
	}
	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override void Flush() => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
