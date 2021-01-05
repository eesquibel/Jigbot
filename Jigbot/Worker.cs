using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Jigbot.Modules;
using Jigbot.Services;
using Jigbot.States;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Jigbot
{
    public class Worker : BackgroundService, IDisposable
    {
        private readonly ILogger<Worker> logger;
        private DiscordSocketClient discord;
        private CommandService command;
        private ServiceProvider services;

        public Worker(ILogger<Worker> logger)
        {
            this.logger = logger;
        }

        private async Task Discord_MessageUpdated(Cacheable<IMessage, ulong> cacheableBefore, SocketMessage after, ISocketMessageChannel channel)
        {
            // If original message not in cache, too old and we don't care about it
            if (!cacheableBefore.HasValue)
            {
                return;
            }

            // No embeds
            if (after.Embeds.Count == 0)
            {
                return;
            }

            var before = cacheableBefore.Value;

            var uploads = services.GetService<UploadService>();
            var config = services.GetService<ConfigService>();

            // Upload directory not set, nothing to do
            if (!uploads.Enabled)
            {
                return;
            }

            // Is a message we care about?
            if ((after.Content ?? before.Content).StartsWith($"!{config.Command}", StringComparison.InvariantCultureIgnoreCase))
            {
                // Are there new embeds able?
                if (after.Embeds.Count <= before.Embeds.Count)
                {
                    return;
                }

                // Have we processed this message before?
                if (before.Reactions.Values.Any(reaction => reaction.IsMe))
                {
                    return;
                }

                await uploads.GetEmbeds(after);
            }
        }

        private async Task Discord_MessageReceived(SocketMessage rawMessage)
        {
            // Only respond to user messages
            if (!(rawMessage is SocketUserMessage message))
            {
                return;
            }

            // The bot should never respond to itself.
            if (message.Author.Id == discord.CurrentUser.Id)
            {
                return;
            }

            int argPos = 0;

            if (!message.HasCharPrefix('!', ref argPos))
            {
                return;
            }

            var context = new SocketCommandContext(discord, message);

            await command.ExecuteAsync(context, argPos, services);
        }

        private Task Discord_Ready()
        {
            logger.LogInformation($"{discord.CurrentUser} is connected!");
            return Task.CompletedTask;
        }

        private Task Discord_Log(LogMessage arg)
        {
            logger.LogInformation(arg.ToString());
            return Task.CompletedTask;
        }

        private ServiceProvider ConfigureServices()
        {
            return ConfigureServices
            (
                discordSocketClient: new DiscordSocketClient(),
                commandService: new CommandService()
            );
        }

        private ServiceProvider ConfigureServices(DiscordSocketClient discordSocketClient, CommandService commandService)
        {
            return new ServiceCollection()
                .AddSingleton<ILogger>(logger)
                .AddSingleton(discordSocketClient)
                .AddSingleton(commandService)
                .AddSingleton<ConfigService>()
                .AddSingleton<ImagesState>()
                .AddSingleton<HistoryState>()
                .AddSingleton<RandomizeState>()
                .AddSingleton<NoManageMessagesState>()
                .AddSingleton<RandomImageService>()
                .AddSingleton<ManageMessagesService>()
                .AddSingleton<UploadService>()
                .AddSingleton<PurgeModule>()
                .AddSingleton<RandomModule>()
                .BuildServiceProvider();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (discord = new DiscordSocketClient(new DiscordSocketConfig
            {
                /**
                 * Increases memory usage, but needed for the !purgeall command,
                 * and to get embeds that are parsed asynchonously and sent via MESSAGE_UPDATE event
                 */
                MessageCacheSize = 128,
            }))
            {
                discord.Log += Discord_Log;
                discord.Ready += Discord_Ready;

                command = new CommandService(new CommandServiceConfig()
                {

                });

                using (services = ConfigureServices(discord, command))
                {
                    var randomImage = services.GetService<RandomImageService>();
                    var randomize = services.GetService<RandomizeState>();
                    var config = services.GetService<ConfigService>();

                    await command.AddModulesAsync(Assembly.GetEntryAssembly(), services);
                    await command.CreateModuleAsync(config.Command, builder =>
                    {
                        builder.AddCommand("", (context, param, service, command) =>
                        {
                            return CommandModule.Execute(context, param, service, command);
                        }, builder =>
                        {

                        });
                    });

                    discord.MessageReceived += Discord_MessageReceived;
                    discord.MessageUpdated += Discord_MessageUpdated;

                    await discord.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("Token"));
                    await discord.StartAsync();

                    while (!stoppingToken.WaitHandle.WaitOne(5000))
                    {
                        foreach (var (channel, state) in randomize.Where(pair => pair.Value != RandomStatus.Off))
                        {
                            await randomImage.RandomImage(channel, state);
                        }
                    }

                    discord.MessageReceived -= Discord_MessageReceived;
                    discord.MessageUpdated -= Discord_MessageUpdated;
                }

                await discord.StopAsync();
                await discord.LogoutAsync();
            }
        }
    }
}
