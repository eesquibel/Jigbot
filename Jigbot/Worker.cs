using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Jigbot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly DiscordSocketClient discord;
        private readonly Random random;
        private readonly string Command;
        private readonly string UriBase;
        private JArray Data;

        public Worker(ILogger<Worker> logger)
        {
            this.logger = logger;
            Command = Environment.GetEnvironmentVariable("Command");
            UriBase = Environment.GetEnvironmentVariable("URIBASE");

            random = new Random();

            discord = new DiscordSocketClient();
            discord.Log += Discord_Log;
            discord.Ready += Discord_Ready;
            discord.MessageReceived += Discord_MessageReceived;
        }

        private async Task Discord_MessageReceived(SocketMessage message)
        {
            // The bot should never respond to itself.
            if (message.Author.Id == discord.CurrentUser.Id)
            {
                return;
            }

            if (message.Content == "!" + Command)
            {
                var index = random.Next(0, Data.Count - 1);
                var file = Data[index]["name"].ToString();

                var request = WebRequest.Create(UriBase + file);
                using (var response = await request.GetResponseAsync())
                {
                    await message.Channel.SendFileAsync(response.GetResponseStream(), file);
                }
            }
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var command = Environment.GetEnvironmentVariable("command");
            var uriBase = Environment.GetEnvironmentVariable("URIBASE");
            var env = Environment.GetEnvironmentVariables();

            var request = WebRequest.Create(UriBase);
            using (var response = await request.GetResponseAsync())
            {
                using (var reader = new StreamReader(response.GetResponseStream()))
                {
                    using (var json = new JsonTextReader(reader))
                    {
                        Data = (JArray)await JToken.ReadFromAsync(json, stoppingToken);
                    }
                }
            }

            logger.LogInformation($"Found {Data.Count} images");

            await discord.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("Token"));
            await discord.StartAsync();

            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
