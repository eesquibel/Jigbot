using Discord;
using Discord.WebSocket;
using Jigbot.States;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Jigbot.Services
{
    public class RandomImageService
    {
        private ILogger logger;
        private ImagesState Images;
        private HistoryState History;
        private DiscordSocketClient Discord;
        private ManageMessagesService ManageMessages;
        private RandomizeFrequency RandomFrequency;

        private readonly Random random = new Random();

        public RandomImageService(IServiceProvider services)
        {
            logger = services.GetService<ILogger>();
            Images = services.GetService<ImagesState>();
            History = services.GetService<HistoryState>();
            Discord = services.GetService<DiscordSocketClient>();
            ManageMessages = services.GetService<ManageMessagesService>();
            RandomFrequency = services.GetService<RandomizeFrequency>();
        }

        public async Task RandomImage(ulong channel, RandomStatus state)
        {
            if (state == RandomStatus.Off)
            {
                return;
            }

            var frequency = RandomFrequency.GetOrAdd(channel, 720 * 4);

            if (random.Next((int)frequency) != 1)
            {
                return;
            }

            var spoiler = false;

            if (state == RandomStatus.Safe)
            {
                var now = DateTime.UtcNow;
                if (now.Hour > 15 || now.Hour < 1)
                {
                    spoiler = true;
                }
            }

            await RandomImage(channel, spoiler);
        }

        public Task RandomImage(IUserMessage message)
        {
            return RandomImage(message, false);
        }

        public async Task RandomImage(IUserMessage message, bool spoiler)
        {
            var result = await RandomImage(message.Channel, message.Author.Mention, spoiler);

            if (result != null)
            {
                logger.LogInformation($"User {message.Author.Username} <#{message.Author.Id}> requested random image");
                History.AddOrUpdate(message.Author.Id, result.Id, (key, old) => result.Id);

                await ManageMessages.DeleteCommandMessage(message);
            }
        }

        public Task<IUserMessage> RandomImage(ulong channelId)
        {
            var channel = Discord.GetChannel(channelId);

            if (channel is IMessageChannel messageChannel)
            {
                return RandomImage(messageChannel, null, false);
            }

            throw new Exception("Unsupported Channel");
        }

        public Task<IUserMessage> RandomImage(ulong channelId, bool spoiler)
        {
            var channel = Discord.GetChannel(channelId);

            if (channel is IMessageChannel messageChannel)
            {
                return RandomImage(messageChannel, null, spoiler);
            }

            throw new Exception("Unsupported Channel");
        }

        public Task<IUserMessage> RandomImage(IMessageChannel channel)
        {
            return RandomImage(channel, null, false);
        }

        public async Task<IUserMessage> RandomImage(IMessageChannel channel, string text = null, bool spoiler = false)
        {
            var index = random.Next(0, Images.Count - 1);
            var file = Images[index];

            switch (Images.Scheme)
            {
                case "file":
                    logger.LogInformation($"RandomImage for {channel.Name} <#{channel.Id}>: {file}");
                    return await channel.SendFileAsync(file, text, false, null, null, spoiler);
                case "http":
                case "https":
                    var request = WebRequest.Create(Images.UriBase + file);
                    logger.LogInformation($"RandomImage for {channel.Name} <#{channel.Id}>: {Images.UriBase + file}");
                    using (var response = await request.GetResponseAsync())
                    {
                        return await channel.SendFileAsync(response.GetResponseStream(), file, text, false, null, null, spoiler);
                    }
            }

            throw new Exception("Unknown Scheme");
        }

    }
}
