using Microsoft.Extensions.Time.Testing;
using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;

namespace Orabot.Tests.Workers
{
	internal sealed class WorkerTestClock : FakeTimeProvider
	{
		private readonly Channel<TimeSpan> _scheduledDelays = Channel.CreateUnbounded<TimeSpan>();

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			var timer = base.CreateTimer(callback, state, dueTime, period);
			_scheduledDelays.Writer.TryWrite(dueTime);
			return timer;
		}

		internal async Task AdvanceNextDelayAsync(TimeSpan expectedDelay)
		{
			var delay = await _scheduledDelays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
			Assert.Equal(expectedDelay, delay);
			Advance(delay);
		}

		internal Task<TimeSpan> WaitForDelayAsync() =>
			_scheduledDelays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
	}
}
