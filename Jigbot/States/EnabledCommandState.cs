using Jigbot.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Jigbot.States
{
    public class EnabledCommandState : ConcurrentDictionary<ulong, string>
    {
        private readonly ConfigService configService;

        public EnabledCommandState(ConfigService configService, ILogger logger)
        {
            this.configService = configService;

            configService.GetRange("Command").ContinueWith(task =>
            {
                if (task.Exception != null)
                {
                    logger.LogError(task.Exception, task.Exception.Message);
                    return;
                }

                var channelMatch = new Regex($@"^.*{Regex.Escape("/Command/")}(?<channel>[\d]+)$");

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

                    logger.LogInformation($"Loading configuration from {Key}");

                    AddOrUpdate(channel, Value, (k, o) => Value);
                }
            });
        }

        public new bool TryUpdate(ulong key, string newValue, string comparisonValue)
        {
            var result = base.TryUpdate(key, newValue, comparisonValue);

            configService.Put($"Command/{key}", newValue);

            return result;
        }       
    }
}
