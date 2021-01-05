using Jigbot.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Jigbot.States
{
    public enum RandomStatus : byte
    {
        Off,
        On,
        Safe
    }

    public class RandomizeState : ConcurrentDictionary<ulong, RandomStatus>
    {
        private readonly ConfigService configService;

        public RandomizeState(ConfigService configService, ILogger logger)
        {
            this.configService = configService;

            configService.GetRange("Randomize").ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    logger.LogError(task.Exception, task.Exception.Message);
                    return;
                }

                var channelMatch = new Regex($@"^.*{Regex.Escape($"/Randomize/")}(?<channel>[\d]+)$");

                foreach (var (Key, Value) in task.Result)
                {
                    ulong channel;
                    RandomStatus randomize;

                    var match = channelMatch.Match(Key);

                    if (!match.Success)
                    {
                        continue;
                    }

                    if (!ulong.TryParse(match.Groups["channel"].Value, out channel))
                    {
                        continue;
                    }

                    if (!Enum.TryParse(Value, out randomize))
                    {
                        continue;
                    }

                    logger.LogInformation($"Loading configuration from {Key}");

                    AddOrUpdate(channel, randomize, (k, o) => randomize);
                }
            });
        }
        public new bool TryUpdate(ulong key, RandomStatus newValue, RandomStatus comparisonValue)
        {
            var result = base.TryUpdate(key, newValue, comparisonValue);

            configService.Put($"Randomize/{key}", newValue);

            return result;
        }       
    }
}
