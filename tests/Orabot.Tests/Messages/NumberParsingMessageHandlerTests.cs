using Discord;
using Orabot.Bot.Discord.Messages;

namespace Orabot.Tests.Messages;

public sealed class NumberParsingMessageHandlerTests
{
	[Fact]
	public void Matches_are_case_insensitive_and_respect_each_keyword_threshold()
	{
		var parser = new Parser();
		Assert.Equal(new[] { "100", "200" }, parser.Parse("ora#99 ORA#100 mod#199 MOD#200"));
	}

	[Fact]
	public void Repeated_references_are_distinct_and_work_is_capped_at_three_numbers()
	{
		var values = new Parser().Parse("ora#100 ORA#100 ora#101 ora#102 ora#103 mod#200");
		Assert.Equal(new[] { "100", "101", "102" }, values);
		Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
	}

	[Theory]
	[InlineData("prefixora#100")]
	[InlineData("ora#2147483648")]
	[InlineData("ora#99 mod#199")]
	public void Invalid_boundaries_overflow_and_below_threshold_numbers_are_ignored(string text) =>
		Assert.Empty(new Parser().Parse(text));

	sealed class Parser : BaseNumberParsingMessageHandler
	{
		protected override Dictionary<string, int> MinimumHandledNumberPerKeyword => new() { ["ora"] = 100, ["mod"] = 200 };
		public string[] Parse(string text) => GetMatchedNumbers(text).ToArray();
		public override Task InvokeAsync(IUserMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}
}
