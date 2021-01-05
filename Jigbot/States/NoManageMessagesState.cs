using Discord;
using Discord.Net;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Jigbot.States
{
    public class NoManageMessagesState : ConcurrentBag<ulong>
    {

    }
}
