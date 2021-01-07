using Jigbot.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Jigbot.States
{
    public class RandomizeFrequency : ConcurrentDictionary<ulong, uint>
    {
        private readonly ConfigService configService;

        public RandomizeFrequency(ConfigService configService, ILogger logger)
        {
            this.configService = configService;

            configService.GetRange("Frequency").ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    logger.LogError(task.Exception, task.Exception.Message);
                    return;
                }

                var channelMatch = new Regex($@"^.*{Regex.Escape($"/Frequency/")}(?<channel>[\d]+)$");

                foreach (var (Key, Value) in task.Result)
                {
                    ulong channel;

                    var match = channelMatch.Match(Key);

                    if (!match.Success)
                    {
                        continue;
                    }

                    if (!ulong.TryParse(match.Groups["channel"].Value, out channel))
                    {
                        continue;
                    }

                    if (!uint.TryParse(Value, out uint frequency))
                    {
                        continue;
                    }

                    logger.LogInformation($"Loading configuration from {Key}");

                    AddOrUpdate(channel, frequency, (k, o) => frequency);
                }
            });
        }
        public new bool TryUpdate(ulong key, uint newValue, uint comparisonValue)
        {
            var result = base.TryUpdate(key, newValue, comparisonValue);

            configService.Put($"Frequency/{key}", newValue);

            return result;
        }       
    }
}
