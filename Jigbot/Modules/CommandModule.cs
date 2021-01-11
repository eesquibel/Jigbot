using Discord;
using Discord.Commands;
using Jigbot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jigbot.Modules
{
    public static class CommandModule
    {
        private static readonly Regex hasUrl = new Regex(@"https?://.*\.(jpg|jpeg|gif|png|webp|webm)", RegexOptions.IgnoreCase);

        private static readonly Emoji Check = new Emoji("\u2611\uFE0F");

        public static async Task Execute(ICommandContext Context, object[] param, IServiceProvider service, CommandInfo command)
        {
            var logger = service.GetService<ILogger>();
            var randomImage = service.GetService<RandomImageService>();
            var uploads = service.GetService<UploadService>();

            if (Context.Message.Attachments.Count > 0 && uploads.Enabled)
            {
                byte i = 0;
                var tasks = new Task[Context.Message.Attachments.Count];

                foreach (var attachment in Context.Message.Attachments)
                {
                    tasks[i++] = uploads.Get(attachment);
                }

                Task.WaitAll(tasks);

                try
                {
                    await Context.Message.AddReactionAsync(Check);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }

                return;
            }

            if (Context.Message.Embeds.Count > 0 && uploads.Enabled)
            {
                await uploads.GetEmbeds(Context.Message);
                return;
            }

            if (Context.Message.Attachments.Count == 0 && Context.Message.Embeds.Count == 0)
            {
                if (param[0] is string message)
                {
                    if (hasUrl.IsMatch(message))
                    {
                        return;
                    }
                }

                await randomImage.RandomImage(Context.Message);
                return;
            }
        }
    }
}
