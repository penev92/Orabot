using Orabot.Bot.Infrastructure.Attachments;
using System.Net;

namespace Orabot.Tests.Attachments;

/// <summary>Verifies actual-byte limits, cancellation, restricted CDN sources and archive-path ownership without network requests.</summary>
public sealed class AttachmentDownloaderTests
{
	[Fact]
	public async Task Generated_archive_path_ignores_uploaded_path_components_and_keeps_complete_bytes()
	{
		using var files = new AttachmentFiles();
		var body = new byte[] { 1, 2, 3, 4 };
		using var http = new AttachmentHttp(body);
		var result = await new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(body.Length, "../../outside/evil.log"), files.Root, 32, ".log");
		Assert.Equal(files.Root, Path.GetDirectoryName(result.FilePath));
		Assert.Matches("^[a-f0-9]{32}\\.log$", Path.GetFileName(result.FilePath));
		Assert.Equal("../../outside/evil.log", result.OriginalFileName);
		Assert.Equal(body, await File.ReadAllBytesAsync(result.FilePath));
		Assert.Single(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Advertised_oversize_is_rejected_before_http_or_file_creation()
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(new byte[] { 1 });
		await Assert.ThrowsAsync<InvalidDataException>(() => new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(33), files.Root, 32, ".log"));
		Assert.Equal(0, http.Requests);
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Actual_bytes_are_limited_when_length_is_missing_or_falsely_small(bool falseLength)
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(_ =>
		{
			var content = new StreamingContent(new MemoryStream(new byte[33]));
			if (falseLength) content.Headers.ContentLength = 1;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
		});
		await Assert.ThrowsAsync<InvalidDataException>(() => new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(0), files.Root, 32, ".log"));
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Truncated_body_does_not_become_an_archive()
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(_ =>
		{
			var content = new StreamingContent(new MemoryStream(new byte[3]));
			content.Headers.ContentLength = 10;
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
		});
		await Assert.ThrowsAsync<InvalidDataException>(() => new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(10), files.Root, 32, ".log"));
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Cancellation_interrupts_body_read_and_removes_partial_output()
	{
		using var files = new AttachmentFiles();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var http = new AttachmentHttp(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{ Content = new StreamingContent(new CancellableBody(entered)) }));
		using var cancellation = new CancellationTokenSource();
		var download = new AttachmentDownloader(http).DownloadAsync(AttachmentFixtures.Attachment(), files.Root, 32, ".log", cancellation.Token);
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(TimeSpan.FromSeconds(5)));
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	[Theory]
	[InlineData("http://cdn.discordapp.com/attachments/a/b/file")]
	[InlineData("https://cdn.discordapp.com.evil.invalid/attachments/a/b/file")]
	[InlineData("https://user:secret@cdn.discordapp.com/attachments/a/b/file")]
	[InlineData("https://cdn.discordapp.com:444/attachments/a/b/file")]
	[InlineData("https://example.org/file")]
	[InlineData("https://cdn.discordapp.com/other/file")]
	public async Task Unsupported_sources_are_rejected_without_fetching(string url)
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(Array.Empty<byte>());
		var error = await Assert.ThrowsAsync<InvalidDataException>(() => new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(url: url), files.Root, 32, ".log"));
		Assert.DoesNotContain(url, error.Message);
		Assert.Equal(0, http.Requests);
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Redirect_response_is_not_followed_or_archived()
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
			{ Content = new ByteArrayContent(Array.Empty<byte>()), Headers = { Location = new Uri("https://example.org/private") } }));
		await Assert.ThrowsAsync<InvalidDataException>(() => new AttachmentDownloader(http).DownloadAsync(
			AttachmentFixtures.Attachment(), files.Root, 32, ".log"));
		Assert.Equal(1, http.Requests);
		Assert.Empty(Directory.GetFiles(files.Root));
	}
}
