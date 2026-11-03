using Discord;
using Microsoft.Extensions.Options;
using Orabot.Bot.Features.Logs;
using Orabot.Bot.Infrastructure.Attachments;
using System.Text;

namespace Orabot.Tests.Attachments;

/// <summary>Preserves log previews and explanations while bounding output and passing cancellation to Discord writes.</summary>
public sealed class LogAttachmentTests
{
	[Fact]
	public async Task Preview_and_last_exception_explanation_keep_the_existing_format()
	{
		using var files = new AttachmentFiles();
		const string text = "start\r\nException of type System.Exception: First\r\nException of type System.Exception: Last\r\n";
		var body = Encoding.UTF8.GetBytes(text);
		using var http = new AttachmentHttp(body);
		var transformer = Transformer(http, files);
		var result = await transformer.CreateRawMessageAsync(AttachmentFixtures.Attachment(body.Length));
		Assert.Equal("```" + text + "```", result.RawMessage);
		Assert.Equal(text, result.FullText);
		Assert.True(transformer.TryCreateExceptionExplanationMessage(text, out var explanation));
		Assert.Equal("Now, I'm no expert, but I suspect your problem is *probably*  related to this:\r\n> **Last**", explanation);
		Assert.Single(Directory.GetFiles(files.Root));
	}

	[Fact]
	public async Task Preview_stays_at_1000_characters_and_whitespace_logs_are_still_archived()
	{
		using var files = new AttachmentFiles();
		using (var http = new AttachmentHttp(Encoding.UTF8.GetBytes(new string('x', 1200))))
		{
			var result = await Transformer(http, files).CreateRawMessageAsync(AttachmentFixtures.Attachment(1200));
			Assert.Equal("```" + new string('x', 1000) + "```", result.RawMessage);
		}
		using (var http = new AttachmentHttp(Encoding.UTF8.GetBytes(" \r\n")))
		{
			var result = await Transformer(http, files).CreateRawMessageAsync(AttachmentFixtures.Attachment(3));
			Assert.Null(result.RawMessage);
		}
		Assert.Equal(2, Directory.GetFiles(files.Root).Length);
	}

	[Fact]
	public async Task Log_handler_bounds_combined_output_and_disables_mentions()
	{
		using var files = new AttachmentFiles();
		var body = Encoding.UTF8.GetBytes("Exception of type System.Exception: " + new string('x', 4000));
		using var http = new AttachmentHttp(body);
		string? sent = null;
		RequestOptions? sentOptions = null;
		AllowedMentions? mentions = null;
		var message = AttachmentFixtures.Message(AttachmentFixtures.Attachment(body.Length), "", (text, _, allowed, options) =>
		{
			sent = text; mentions = allowed; sentOptions = options;
		});
		var handler = new LogFileAttachmentMessageHandler(Transformer(http, files));
		Assert.True(handler.CanHandle(message));
		await handler.InvokeAsync(message);
		Assert.NotNull(sent);
		Assert.InRange(sent.Length, 1006, 2000);
		Assert.EndsWith("...**", sent);
		Assert.NotNull(sentOptions);
		Assert.True(sentOptions.CancelToken.CanBeCanceled);
		Assert.NotNull(mentions);
		AssertNoMentions(mentions);
	}

	[Theory]
	[InlineData("Exception of type System.Exception: Details")]
	[InlineData("Exception of type `System.Exception`: Details")]
	public async Task Stack_trace_responses_preserve_recognition_and_use_cancellable_safe_writes(string input)
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(Array.Empty<byte>());
		using var cancellation = new CancellationTokenSource();
		string? sent = null;
		var message = AttachmentFixtures.Message(null!, input, (text, _, allowed, options) =>
		{
			sent = text;
			Assert.Equal(cancellation.Token, options.CancelToken);
			AssertNoMentions(allowed);
		});
		var handler = new StackTraceMessageHandler(Transformer(http, files));
		Assert.True(handler.CanHandle(message));
		await handler.InvokeAsync(message, cancellation.Token);
		Assert.EndsWith("**Details**", sent);
		Assert.Equal(0, http.Requests);
	}

	[Fact]
	public async Task Log_limit_is_enforced_before_any_download()
	{
		using var files = new AttachmentFiles();
		using var http = new AttachmentHttp(Array.Empty<byte>());
		await Assert.ThrowsAsync<InvalidDataException>(() => Transformer(http, files).CreateRawMessageAsync(
			AttachmentFixtures.Attachment(AttachmentLogFileToMessageTransformer.MaximumLogBytes + 1)));
		Assert.Equal(0, http.Requests);
		Assert.Empty(Directory.GetFiles(files.Root));
	}

	static void AssertNoMentions(AllowedMentions mentions)
	{
		// Discord.Net's canonical None object leaves nullable flags unspecified and has no explicit recipients.
		Assert.Equal(AllowedMentions.None.AllowedTypes, mentions.AllowedTypes);
		Assert.Empty(mentions.UserIds ?? new List<ulong>());
		Assert.Empty(mentions.RoleIds ?? new List<ulong>());
		Assert.False(mentions.MentionRepliedUser ?? false);
	}

	static AttachmentLogFileToMessageTransformer Transformer(AttachmentHttp http, AttachmentFiles files) =>
		new(new AttachmentDownloader(http), Options.Create(new LogOptions { LogStorageFolder = files.Root }));
}
