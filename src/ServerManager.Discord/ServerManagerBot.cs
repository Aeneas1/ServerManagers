﻿using Discord;
using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.Net.Providers.WS4Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServerManagerTool.Discord.Delegates;
using ServerManagerTool.Discord.Interfaces;
using ServerManagerTool.Discord.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ServerManagerTool.Discord
{
    public sealed class ServerManagerBot : IServerManagerBot
    {
        internal ServerManagerBot()
        {
        }

        private bool Started
        {
            get;
            set;
        }

        public async Task StartAsync(string commandPrefix, string discordToken, string dataDirectory, HandleCommandDelegate handleCommandCallback, CancellationToken token)
        {
            if (Started)
            {
                return;
            }
            Started = true;

            if (string.IsNullOrWhiteSpace(commandPrefix) || string.IsNullOrWhiteSpace(discordToken))
            {
                return;
            }

            if (commandPrefix.Any(c => !char.IsLetterOrDigit(c)))
            {
                throw new Exception("#DiscordBot_InvalidPrefixError");
            }

            if (!commandPrefix.EndsWith(DiscordBot.PREFIX_DELIMITER))
            {
                commandPrefix += DiscordBot.PREFIX_DELIMITER;
            }

            var settings = new Dictionary<string, string>
            {
                { "DiscordSettings:Prefix", commandPrefix },
                { "DiscordSettings:Token", discordToken },
                { "ServerManager:DataDirectory", dataDirectory }
            };

            // Begin building the configuration file
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var socketConfig = new DiscordSocketConfig
            {
#if DEBUG
                LogLevel = LogSeverity.Verbose,
#else
                LogLevel = LogSeverity.Info,
#endif
                // Tell Discord.Net to cache 1000 messages per channel
                MessageCacheSize = 1000,
            };
            if (Environment.OSVersion.Version < new Version(6, 2))
            {
                // windows 7 or early
                socketConfig.WebSocketProvider = WS4NetProvider.Instance;
            }

            var commandConfig = new CommandServiceConfig
            {
                // Force all commands to run async
                DefaultRunMode = RunMode.Async,
#if DEBUG
                LogLevel = LogSeverity.Verbose,
#else
                LogLevel = LogSeverity.Info,
#endif
            };

            // Build the service provider
            var services = new ServiceCollection()
                // Add the discord client to the service provider
                .AddSingleton(new DiscordSocketClient(socketConfig))
                // Add the command service to the service provider
                .AddSingleton(new CommandService(commandConfig))
                // Add remaining services to the provider
                .AddSingleton<CommandHandlerService>()
                .AddSingleton<InteractiveService>()
                .AddSingleton<LoggingService>()
                .AddSingleton<StartupService>()
                .AddSingleton<ShutdownService>()
                .AddSingleton<Random>()
                .AddSingleton(config);

            // Create the service provider
            using (var provider = services.BuildServiceProvider())
            {
                // Initialize the logging service, startup service, and command handler
                provider?.GetRequiredService<LoggingService>();
                await provider?.GetRequiredService<StartupService>().StartAsync();
                provider?.GetRequiredService<CommandHandlerService>();

                DiscordBot.HandleCommandCallback = handleCommandCallback;

                try
                {
                    // Prevent the application from closing
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (TaskCanceledException)
                {
                    Debug.WriteLine("Task Canceled");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("Operation Canceled");
                }

                await provider?.GetRequiredService<ShutdownService>().StopAsync();
            }
        }
    }
}
