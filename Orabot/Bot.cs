﻿using System;
using System.Configuration;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Orabot.EventHandlers.Abstraction;

namespace Orabot
{
	public class Bot : IDisposable
	{
		private static readonly string DiscordBotToken = ConfigurationManager.AppSettings["BotToken"];

		private readonly IServiceProvider _serviceProvider;

		private readonly DiscordSocketClient _client;
		private readonly CommandService _commands;

		private readonly ILogEventHandler _logEventHandler;
		private readonly IMessageEventHandler _messageEventHandler;

		public Bot(IServiceProvider serviceProvider)
		{
			_serviceProvider = serviceProvider;

			_client = _serviceProvider.GetService<DiscordSocketClient>();
			_commands = _serviceProvider.GetService<CommandService>();
			_logEventHandler = _serviceProvider.GetService<ILogEventHandler>();
			_messageEventHandler = _serviceProvider.GetService<IMessageEventHandler>();

			AttachEventHandlers();
			RegisterCommandModules();
		}

		public async Task RunAsync()
		{
			await _client.LoginAsync(TokenType.Bot, DiscordBotToken);
			await _client.StartAsync();

			Console.ReadLine();

			await _client.LogoutAsync();
			await _client.StopAsync();

			Console.WriteLine("Done");
			Console.ReadLine();
		}

		public void Dispose()
		{
			((IDisposable) _commands)?.Dispose();
			_client?.Dispose();
		}

		#region Private methods

		private void AttachEventHandlers()
		{
			_client.Log += _logEventHandler.Log;
			_client.MessageReceived += _messageEventHandler.HandleMessageReceivedAsync;
		}

		private void RegisterCommandModules()
		{
			var modules = _serviceProvider.GetServices(typeof(ModuleBase<SocketCommandContext>));
			foreach (var module in modules)
			{
				_commands.AddModuleAsync(module.GetType(), _serviceProvider).Wait();
			}
		}

		#endregion
	}
}
