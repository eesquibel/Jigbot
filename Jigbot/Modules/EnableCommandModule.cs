using Discord;
using Discord.Commands;
using Jigbot.Services;
using Jigbot.States;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Jigbot.Modules
{
    public class EnableCommandModule : ModuleBase<SocketCommandContext>
    {
        public ILogger logger { get; set; }
        
        public ConfigService Config { get; set; }

        public ManageMessagesService ManageMessages { get; set; }
        
        public EnabledCommandState CommandState { get; set; }

        private string Current;
        
        protected override void BeforeExecute(CommandInfo command)
        {
            base.BeforeExecute(command);
            Current = CommandState.GetOrAdd(Context.Channel.Id, (string)null);
        }

        protected override void AfterExecute(CommandInfo command)
        {
            base.AfterExecute(command);

            ManageMessages.DeleteCommandMessage(Context.Message).GetAwaiter().GetResult();

            logger.LogInformation($"User {Context.Message.Author.Username} (#{Context.Message.Author.Id}) requested {command.Name}");
        }

        [Command("enable")]
        [RequireUserPermission(ChannelPermission.ManageChannels)]
        public async Task Enable(string command)
        {
            command = command.Trim().ToLower();

            var index = Config.GetIndex(command);

            if (index >= 0)
            {
                if (CommandState.TryUpdate(Context.Channel.Id, command, Current))
                {
                    await ReplyAsync($"Command is now {command}");
                }
                else
                {
                    await ReplyAsync($"Command is already {command}");
                }
            } else
            {
                await ReplyAsync($"Command not found ({command})");
            }
        }

        [Command("enable")]
        public async Task Usage()
        {
            ChannelPermissions perms;
            var user = Context.User as IGuildUser;

            await ReplyAsync($"Command is {Current ?? "_not set_"}");

            if (Context.Channel is IGuildChannel guildChannel)
                perms = user.GetPermissions(guildChannel);
            else
                perms = ChannelPermissions.All(Context.Channel);

            if (perms.Has(ChannelPermission.ManageChannels))
            {
                await ReplyAsync($"!enable {string.Join("|", Config.Command)}");
            }
        }
    }
}
