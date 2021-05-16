using Discord;
using Discord.Commands;
using Jigbot.Services;
using Jigbot.States;
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

        public static Task Execute(ICommandContext Context, object[] param, IServiceProvider service, CommandInfo command)
        {
            var logger = service.GetService<ILogger>();
            var randomImage = service.GetService<RandomImageService>();
            var uploads = service.GetService<UploadService>();
            var random = service.GetService<RandomizeState>();
            var config = service.GetService<ConfigService>();
            var index = config.GetIndex(command.Aliases[0]);

            if (index == -1)
            {
                return Task.CompletedTask;
            }

            if (Context.Message.Attachments.Count > 0 && uploads.Enabled)
            {
                var tasks = new Task[Context.Message.Attachments.Count];
                byte i = 0;

                foreach (var attachment in Context.Message.Attachments)
                {
                   tasks[i++] = uploads.Get(attachment, index);
                }

                Task.WhenAll(tasks).ContinueWith((task) =>
                {
                    try
                    {
                        if (task.IsCompletedSuccessfully)
                        {
                            _ = Context.Message.AddReactionAsync(Check);
                        }
                        else
                        {
                            throw task.Exception;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                });


                return Task.CompletedTask;
            }

            if (Context.Message.Embeds.Count > 0 && uploads.Enabled)
            {
                _ = uploads.GetEmbeds(Context.Message, index);
                return Task.CompletedTask;
            }

            if (Context.Message.Attachments.Count == 0 && Context.Message.Embeds.Count == 0)
            {
                if (param.Length > 0)
                {
                    if (param[0] is string message)
                    {
                        if (hasUrl.IsMatch(message))
                        {
                            return Task.CompletedTask;
                        }
                    }
                }

                var state = random.GetOrAdd(Context.Channel.Id, RandomStatus.Off);
                var spoiler = false;

                if (state == RandomStatus.Safe)
                {
                    var now = DateTime.UtcNow;
                    if (now.Hour > 15 || now.Hour < 1)
                    {
                        spoiler = true;
                    }
                }

                _ = randomImage.RandomImage(Context.Message, spoiler);
            }

            return Task.CompletedTask;
        }
    }
}
