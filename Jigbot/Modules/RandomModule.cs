using Discord.Commands;
using Jigbot.Services;
using Jigbot.States;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Jigbot.Modules
{
    [Group("random")]
    public class RandomModule : ModuleBase<SocketCommandContext>
    {
        public ILogger logger { get; set; }
        public ManageMessagesService ManageMessages { get; set; }
        public RandomizeState Randomize { get; set; }
        
        private RandomStatus State;
        
        protected override void BeforeExecute(CommandInfo command)
        {
            base.BeforeExecute(command);
            State = Randomize.GetOrAdd(Context.Channel.Id, RandomStatus.Off);
        }

        protected override void AfterExecute(CommandInfo command)
        {
            base.AfterExecute(command);
            
            logger.LogInformation($"User {Context.Message.Author.Username} (#{Context.Message.Author.Id}) requested random {command.Name}");
        }

        [Command("on")]
        public async Task On()
        {
            if (Randomize.TryUpdate(Context.Channel.Id, RandomStatus.On, State))
            {
                await ReplyAsync($"Randomizer is now enable");
            }
            else
            {
                await ReplyAsync($"Randomizer is already enable");
            }
        }

        [Command("off")]
        public async Task Off()
        {

            if (Randomize.TryUpdate(Context.Channel.Id, RandomStatus.Off, State))
            {
                await ReplyAsync($"Randomizer is now disabled");
            }
            else
            {
                await ReplyAsync($"Randomizer is already disabled");
            }
        }

        [Command("safe")]
        public async Task Safe()
        {
            if (Randomize.TryUpdate(Context.Channel.Id, RandomStatus.Safe, State))
            {
                await ReplyAsync($"Randomizer is now safe-for-work");
            }
            else
            {
                await ReplyAsync($"Randomizer is already enabled");
            }
        }

        [Command("status")]
        public async Task Status()
        {
            switch (State)
            {
                case RandomStatus.Off:
                    await ReplyAsync("Randomizer is disabled");
                    return;
                case RandomStatus.On:
                    await ReplyAsync("Randomizer is enabled");
                    return;
                case RandomStatus.Safe:
                    await ReplyAsync("Randomizer is safe-for-work");
                    return;
            }
        }

        [Command()]
        public async Task Usage()
        {
            await ReplyAsync($"!random on|off|safe|status");
        }
    }
}
