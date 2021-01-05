using Discord;
using Discord.Net;
using Jigbot.States;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Jigbot.Services
{
    public class ManageMessagesService
    {
        public ILogger logger { get; set; }

        public NoManageMessagesState NoManageMessages { get; set; }

        public ManageMessagesService(NoManageMessagesState noManageMessageState, ILogger logger)
        {
            this.logger = logger;
            NoManageMessages = noManageMessageState;
        }

        public async Task DeleteCommandMessage(IMessage message)
        {
            try
            {
                if (!NoManageMessages.Contains(message.Channel.Id))
                {
                    await message.DeleteAsync();
                }
            }
            catch (HttpException e)
            {
                if (e.HttpCode == HttpStatusCode.Forbidden)
                {
                    NoManageMessages.Add(message.Channel.Id);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
        }
    }
}
