using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orabot.Bot.Discord.BackgroundWork;

namespace Orabot.Bot.Discord.Messages
{
	/// <summary>Admits passive message work to the shared queue and awaits each matching handler.</summary>
	internal sealed class MessageEventHandler : IMessageEventHandler
	{
		readonly IEnumerable<ICustomMessageHandler> handlers;
		readonly BotWorkQueue queue;
		readonly IHostApplicationLifetime lifetime;
		readonly ILogger<MessageEventHandler> logger;

		public MessageEventHandler(IEnumerable<ICustomMessageHandler> handlers, BotWorkQueue queue,
			IHostApplicationLifetime lifetime, ILogger<MessageEventHandler> logger)
		{
			this.handlers = handlers;
			this.queue = queue;
			this.lifetime = lifetime;
			this.logger = logger;
		}

		public Task HandleMessageReceivedAsync(SocketMessage message) => message is IUserMessage userMessage ? HandleAsync(userMessage) : Task.CompletedTask;

		internal Task HandleAsync(IUserMessage message)
		{
			if (message.Author.IsBot || message.Source != MessageSource.User || lifetime.ApplicationStopping.IsCancellationRequested)
				return Task.CompletedTask;
			if (!queue.TryEnqueue(token => ProcessAsync(message, token)))
				logger.LogWarning("Message {MessageId} skipped because the bot work queue is full or stopping.", message.Id);
			return Task.CompletedTask;
		}

		async Task ProcessAsync(IUserMessage message, CancellationToken stoppingToken)
		{
			using var budget = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
			budget.CancelAfter(TimeSpan.FromSeconds(30));
			foreach (var handler in handlers)
			{
				if (budget.IsCancellationRequested) break;
				try
				{
					if (handler.CanHandle(message)) await handler.InvokeAsync(message, budget.Token);
				}
				catch (OperationCanceledException) when (budget.IsCancellationRequested) { break; }
				catch (Exception exception)
				{
					logger.LogWarning("Message {MessageId} handler {Handler} failed ({ExceptionType}).", message.Id, handler.GetType().Name, exception.GetType().FullName);
				}
			}
		}
	}
}
