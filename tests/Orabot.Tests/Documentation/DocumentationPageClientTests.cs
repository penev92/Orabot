using System.Net;
using System.Net.Http;
using System.Text;
using Orabot.Bot.Features.Documentation;

namespace Orabot.Tests.Documentation;

/// <summary>Exercises real bounded HTML handling against fixture transports, never external servers.</summary>
public sealed class DocumentationPageClientTests
{
	[Fact]
	public async Task Finds_heading_with_missing_ids_and_whitespace_and_returns_only_first_paragraph()
	{
		using var http = new HttpClient(new FixtureHandler(_ => new StringContent(
			"<h3>Missing id</h3><p>Wrong</p><h3 id='KNOWN'>Known</h3>\n<!-- gap --><p>First &amp; only.</p><p>Second.</p>")));
		var entry = await new DocumentationPageClient(http).FindEntryAsync(new Uri("https://example.org/traits"), "known", CancellationToken.None);
		Assert.True(entry.IsPresent);
		Assert.Equal("First & only.", entry.Description);
	}

	[Theory]
	[InlineData("<h3>No id</h3><p>Text</p>", false)]
	[InlineData("<h3 id='known'>Known</h3><h3 id='other'>Other</h3><p>Other text</p>", true)]
	public async Task Missing_heading_or_description_is_normal_fallback(string html, bool present)
	{
		using var http = new HttpClient(new FixtureHandler(_ => new StringContent(html)));
		var entry = await new DocumentationPageClient(http).FindEntryAsync(new Uri("https://example.org/traits"), "known", CancellationToken.None);
		Assert.Equal(present, entry.IsPresent);
		Assert.Null(entry.Description);
	}


	[Fact]
	public async Task Rejects_malformed_deeply_nested_html_before_heading_traversal()
	{
		var malformed = string.Concat(Enumerable.Repeat("<div>", 300)) + "<h3 id='known'>Known</h3><p>Deep</p>";
		using var http = new HttpClient(new FixtureHandler(_ => new StringContent(malformed)));
		// The pinned HtmlAgilityPack throws System.Exception for its per-document nesting guard.
		var exception = await Assert.ThrowsAsync<Exception>(() => new DocumentationPageClient(http)
			.FindEntryAsync(new Uri("https://example.org/traits"), "known", CancellationToken.None));
		Assert.Contains("256 nested tags", exception.Message);
	}

	[Fact]
	public async Task Rejects_oversized_known_content_length()
	{
		using var http = new HttpClient(new FixtureHandler(_ => new StringContent(new string('x', DocumentationPageClient.MaximumDocumentBytes + 1))));
		await Assert.ThrowsAsync<InvalidDataException>(() => new DocumentationPageClient(http)
			.FindEntryAsync(new Uri("https://example.org/traits"), "known", CancellationToken.None));
	}

	[Fact]
	public async Task Enforces_streamed_budget_without_content_length()
	{
		using var http = new HttpClient(new FixtureHandler(_ => new UnknownLengthContent(
			new MemoryStream(new byte[DocumentationPageClient.MaximumDocumentBytes + 1]))));
		await Assert.ThrowsAsync<InvalidDataException>(() => new DocumentationPageClient(http)
			.FindEntryAsync(new Uri("https://example.org/traits"), "known", CancellationToken.None));
	}

	[Fact]
	public async Task Cancellation_reaches_body_reads_after_headers_arrive()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cancellation = new CancellationTokenSource();
		using var http = new HttpClient(new FixtureHandler(_ => new UnknownLengthContent(new WaitingStream(entered))));
		var lookup = new DocumentationPageClient(http).FindEntryAsync(new Uri("https://example.org/traits"), "known", cancellation.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lookup.WaitAsync(TimeSpan.FromSeconds(10)));
	}

	private sealed class FixtureHandler(Func<HttpRequestMessage, HttpContent> content) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content(request) });
		}
	}

	private sealed class UnknownLengthContent(Stream input) : HttpContent
	{
		protected override bool TryComputeLength(out long length) { length = 0; return false; }
		protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => input.CopyToAsync(stream);
		protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(input);
		protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) => Task.FromResult(input);
		protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
	}

	private sealed class WaitingStream(TaskCompletionSource entered) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => 0; set => throw new NotSupportedException(); }
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			entered.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return 0;
		}
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override void Flush() => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
