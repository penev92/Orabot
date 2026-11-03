using System.Reflection;

namespace Orabot.Tests.TestDoubles;

/// <summary>Implements public SDK interfaces with explicit test callbacks, without a Discord connection or a mocking package.</summary>
public class DiscordTestProxy : DispatchProxy
{
	Func<string, object?[]?, object?> handler = null!;

	public static T Create<T>(Func<string, object?[]?, object?> handler) where T : class
	{
		var proxy = Create<T, DiscordTestProxy>();
		((DiscordTestProxy)(object)proxy).handler = handler;
		return proxy;
	}

	protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!.Name, args);
}
