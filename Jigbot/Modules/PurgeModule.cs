using Discord;
using Discord.Commands;
using Jigbot.Services;
using Jigbot.States;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;

namespace Jigbot.Modules
{
    public class PurgeModule : ModuleBase<SocketCommandContext>
    {
        public ILogger logger { get; set; }
        public ManageMessagesService ManageMessages { get; set; }
        public HistoryState History { get; set; }

        [Command("purgeall")]
        [RequireBotPermission(GuildPermission.SendMessages)]
        public async Task PurgeAll()
        {
            // Delete what is cached first
            var cachedMessages = Context.Channel.CachedMessages;
            var cached = cachedMessages.Where(cache => cache.Author.Id == Context.Client.CurrentUser.Id);
            foreach (var item in cached)
            {
                await item.DeleteAsync();
                await Task.Delay(1000);
            }

            // If less than 20 messages where in cache
            if (cachedMessages.Count < 20)
            {
                // Then crawl the message history
                var messages = Context.Channel.GetMessagesAsync(limit: 20);
                await foreach (var page in messages)
                {
                    foreach (var item in page)
                    {
                        if (item.Author.Id == Context.Client.CurrentUser.Id)
                        {
                            await item.DeleteAsync();
                            await Task.Delay(1000);
                        }
                    }
                }
            }

            logger.LogInformation($"User {Context.Message.Author.Username} <#{Context.Message.Author.Id}> requested purgeall");

            await ManageMessages.DeleteCommandMessage(Context.Message);
        }

        [Command("purge")]
        [RequireBotPermission(GuildPermission.SendMessages)]
        public async Task Purge()
        {
            if (History.Remove(Context.Message.Author.Id, out ulong id))
            {
                await Context.Channel.DeleteMessageAsync(id);
            }

            logger.LogInformation($"User {Context.Message.Author.Username} <#{Context.Message.Author.Id}> requested purge");

            await ManageMessages.DeleteCommandMessage(Context.Message);
        }

    }
}
