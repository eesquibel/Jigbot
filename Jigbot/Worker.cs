using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
#if ETCD
using dotnet_etcd;
#endif

namespace Jigbot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly DiscordSocketClient discord;
        private readonly Random random;
        private readonly string Command;
        private readonly Uri UriBase;
        private readonly string Uploads;
        private readonly Regex CommandX;
        private readonly ConcurrentBag<ulong> NoManageMessages;
        private readonly WebClient WebClient;
        private readonly Emoji Check;
        private readonly SHA1CryptoServiceProvider SHA1;
        private readonly Regex HasUrl;
#if ETCD
        private readonly string etcdPrefix;
        private readonly EtcdClient etcd;
#endif
        private ConcurrentDictionary<ulong, bool> Randomize;
        private ConcurrentDictionary<ulong, ulong> History;
        private string[] Data;

        public Worker(ILogger<Worker> logger)
        {
            this.logger = logger;
            Command = Environment.GetEnvironmentVariable("Command")?.ToLower();
            UriBase = new Uri(Environment.GetEnvironmentVariable("URIBASE"));

            var uploads = Environment.GetEnvironmentVariable("UPLOADS");

            if (!string.IsNullOrEmpty(uploads))
            {
                if (Directory.Exists(uploads))
                {
                    Uploads = (new DirectoryInfo(uploads)).FullName;
                    WebClient = new WebClient();
                    Check = new Emoji("\u2611\uFE0F");
                    SHA1 = new SHA1CryptoServiceProvider();
                    HasUrl = new Regex(@"https?://[\w\d\-\.\/]+\.(jpg|jpeg|gif|png)", RegexOptions.IgnoreCase);
                }
                else
                {
                    logger.LogError("UPLOADS directory does not exist: {uplodas}", new { uploads });
                }
            }

#if ETCD          
            var etcdUrl = Environment.GetEnvironmentVariable("ETCD");
            logger.LogInformation("ETCD support is compiled");
            if (string.IsNullOrEmpty(etcdUrl))
            {
                logger.LogInformation("ETCD is disabled");
            }
            else
            {
                try
                {
                    logger.LogInformation($"ETCD is using {etcdUrl}");
                    etcdPrefix = $"jigbot/{Command}";
                    etcd = new EtcdClient(etcdUrl);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
#endif

            random = new Random();
            History = new ConcurrentDictionary<ulong, ulong>();
            NoManageMessages = new ConcurrentBag<ulong>();
            Randomize = new ConcurrentDictionary<ulong, bool>();
            CommandX = new Regex(@"\!" + Regex.Escape(Command) + @"[\s]*(.*)", RegexOptions.IgnoreCase);

            discord = new DiscordSocketClient(new DiscordSocketConfig
            {
                /**
                 * Increases memory usage, but needed for the !purgeall command,
                 * and to get embeds that are parsed asynchonously and sent via MESSAGE_UPDATE event
                 */
                MessageCacheSize = 128,
            });

            discord.Log += Discord_Log;
            discord.Ready += Discord_Ready;
            discord.MessageReceived += Discord_MessageReceived;
            discord.MessageUpdated += Discord_MessageUpdated;
        }

        private async Task Discord_MessageUpdated(Cacheable<IMessage, ulong> cacheableBefore, SocketMessage after, ISocketMessageChannel channel)
        {
            // Upload directory not set, nothing to do
            if (string.IsNullOrEmpty(Uploads))
            {
                return;
            }

            // If original message not in cache, too old and we don't care about it
            if (!cacheableBefore.HasValue)
            {
                return;
            }

            // No embeds
            if (after.Embeds.Count == 0)
            {
                return;
            }

            var before = cacheableBefore.Value;

            // Is a message we care about?
            if (CommandX.IsMatch(after.Content ?? before.Content))
            {
                // Are there new embeds able?
                if (after.Embeds.Count <= before.Embeds.Count)
                {
                    return;
                }

                // Have we processed this message before?
                if (before.Reactions.Values.Any(reaction => reaction.IsMe))
                {
                    return;
                }

                await DownloadEmbeds(after);
            }
        }

        private async Task Discord_MessageReceived(SocketMessage message)
        {
            // The bot should never respond to itself.
            if (message.Author.Id == discord.CurrentUser.Id)
            {
                return;
            }

            var channel = message.Channel;
            var content = message.Content.Trim().ToLower();

            if (message.Content == "!purgeall")
            {
                // Delete what is cached first
                var cachedMessages = channel.CachedMessages;
                var cached = cachedMessages.Where(cache => cache.Author.Id == discord.CurrentUser.Id);
                foreach (var item in cached)
                {
                    await item.DeleteAsync();
                    await Task.Delay(1000);
                }

                // If less than 20 messages where in cache
                if (cachedMessages.Count < 20)
                {
                    // Then crawl the message history
                    var messages = channel.GetMessagesAsync(limit: 20);
                    await foreach (var page in messages)
                    {
                        foreach (var item in page)
                        {
                            if (item.Author.Id == discord.CurrentUser.Id)
                            {
                                await item.DeleteAsync();
                                await Task.Delay(1000);
                            }
                        }
                    }
                }

                logger.LogInformation($"User {message.Author.Username} <#{message.Author.Id}> requested purgeall");

                await DeleteCommandMessage(message);

                return;
            }

            if (message.Content == "!purge")
            {
                if (History.Remove(message.Author.Id, out ulong id))
                {
                    await channel.DeleteMessageAsync(id);
                }

                logger.LogInformation($"User {message.Author.Username} <#{message.Author.Id}> requested purge");

                await DeleteCommandMessage(message);

                return;
            }

            if (content.Length >= 7 && content.Substring(0, 7) == "!random")
            {
                var cmd = content.Length >= 9 ? message.Content.Substring(8) : null;
                var enabled = Randomize.GetOrAdd(message.Channel.Id, false);

                switch (cmd)
                {
                    case "status":
                        await message.Channel.SendMessageAsync($"Randomizer is: {(enabled ? "Enable" : "Disabled")}");
                        break;
                    case "on":
                        if (Randomize.TryUpdate(message.Channel.Id, true, false))
                        {
                            await message.Channel.SendMessageAsync($"Randomizer enable");
                            enabled = true;
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync($"Randomizer is already enable");
                        }
                        break;
                    case "off":
                        if (Randomize.TryUpdate(message.Channel.Id, false, true))
                        {
                            await message.Channel.SendMessageAsync($"Randomizer disabled");
                            enabled = false;
                        }
                        else
                        {
                            await message.Channel.SendMessageAsync($"Randomizer is already disable");
                        }
                        break;
                    default:
                        await message.Channel.SendMessageAsync($"!random on|off|status");
                        break;
                }

                logger.LogInformation($"User {message.Author.Username} (#{message.Author.Id}) requested random {cmd}");

#if ETCD
                if (etcd is EtcdClient)
                {
                    try
                    {
                        await etcd.PutAsync($"{etcdPrefix}/Randomizer/{message.Channel.Id}", enabled.ToString());
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }
#endif

                return;
            }

            if (CommandX.IsMatch(message.Content))
            {
                if (message.Attachments.Count > 0)
                {
                    if (Uploads == null)
                    {
                        return;
                    }

                    byte i = 0;
                    var tasks = new Task[message.Attachments.Count];

                    foreach (var attachment in message.Attachments)
                    {
                        tasks[i++] = Download(attachment);
                    }

                    Task.WaitAll(tasks);

                    try
                    {
                        await message.AddReactionAsync(Check);
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, e.Message);
                    }
                }

                if (message.Embeds.Count > 0)
                {
                    if (Uploads == null)
                    {
                        return;
                    }

                    await DownloadEmbeds(message);
                }
                else
                {
                    if (HasUrl.IsMatch(message.Content))
                    {
                        return;
                    }
                }

                if (message.Attachments.Count == 0 && message.Embeds.Count == 0)
                {
                    var result = await RandomImage(message.Channel, message.Author.Mention);

                    if (result != null)
                    {
                        logger.LogInformation($"User {message.Author.Username} <#{message.Author.Id}> requested {Command}");
                        History.AddOrUpdate(message.Author.Id, result.Id, (key, old) => result.Id);

                        if (content == $"!{Command}")
                        {
                            await DeleteCommandMessage(message);
                        }
                    }
                }

                return;
            }
        }

        private async Task DownloadEmbeds(IMessage message)
        {
            byte i = 0;
            var react = false;
            var tasks = new Task[message.Embeds.Count];

            foreach (var embed in message.Embeds)
            {
                var uri = new Uri(embed.Url);
                var info = new FileInfo(uri.LocalPath);

                switch (info.Extension.ToLower())
                {
                    case ".jpg":
                    case ".jpeg":
                    case ".gif":
                    case ".png":
                        logger.LogInformation($"Downloaded {uri} from {message.Author.Username} <#{message.Author.Id}>");
                        tasks[i++] = Download(uri);
                        react = true;
                        break;
                    default:
                        tasks[i++] = Task.FromResult<object>(null);
                        break;
                }
            }

            Task.WaitAll(tasks);

            try
            {
                if (react)
                {
                    await message.AddReactionAsync(Check);
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
        }

        private Task Download(Attachment attachment)
        {
            return Download(new Uri(attachment.Url));
        }

        private Task Download(string uri)
        {
            return Download(new Uri(uri));
        }

        private async Task Download(Uri uri)
        {
            try
            {
                var bytes = await WebClient.DownloadDataTaskAsync(uri);
                var filename = Uploads + "/";
                var file = new FileInfo(uri.LocalPath);
                using (var stream = new MemoryStream(bytes))
                {
                    var hash = await SHA1.ComputeHashAsync(stream);
                    filename += BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
                filename += file.Extension.ToLower();

                await File.WriteAllBytesAsync(filename, bytes);
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
        }

        private async Task DeleteCommandMessage(IMessage message)
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

        private Task<IUserMessage> RandomImage(ulong channelId)
        {
            var channel = discord.GetChannel(channelId);

            if (channel is IMessageChannel messageChannel)
            {
                return RandomImage(messageChannel, null);
            }

            throw new Exception("Unsupported Channel");
        }

        private Task<IUserMessage> RandomImage(IMessageChannel channel)
        {
            return RandomImage(channel, null);
        }

        private async Task<IUserMessage> RandomImage(IMessageChannel channel, string text = null)
        {
            var index = random.Next(0, Data.Length - 1);
            var file = Data[index];

            switch (UriBase.Scheme)
            {
                case "file":
                    logger.LogInformation($"RandomImage for {channel.Name} <#{channel.Id}>: {file}");
                    return await channel.SendFileAsync(file, text);
                case "http":
                case "https":
                    var request = WebRequest.Create(UriBase + file);
                    logger.LogInformation($"RandomImage for {channel.Name} <#{channel.Id}>: {UriBase + file}");
                    using (var response = await request.GetResponseAsync())
                    {
                        return await channel.SendFileAsync(response.GetResponseStream(), file, text);
                    }
            }

            throw new Exception("Unknown Scheme");
        }

        private Task Discord_Ready()
        {
            logger.LogInformation($"{discord.CurrentUser} is connected!");
            return Task.CompletedTask;
        }

        private Task Discord_Log(LogMessage arg)
        {
            logger.LogInformation(arg.ToString());
            return Task.CompletedTask;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            switch (UriBase.Scheme)
            {
                case "file":
                    if (!Directory.Exists(UriBase.LocalPath))
                    {
                        logger.LogError("Directoy does not exist: {UriBase}", new { UriBase });
                        return;
                    }

                    Data = Directory.GetFiles(UriBase.LocalPath, "*.*", SearchOption.TopDirectoryOnly);

                    break;
                case "http":
                case "https":
                    var request = WebRequest.Create(UriBase);
                    using (var response = await request.GetResponseAsync())
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            using (var json = new JsonTextReader(reader))
                            {
                                var data = (JArray)await JToken.ReadFromAsync(json, stoppingToken);
                                Data = data.Select(item => item.Value<string>("name")).ToArray();
                            }
                        }
                    }

                    break;
            }

            logger.LogInformation($"Found {Data.Length} images");

            await discord.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("Token"));
            await discord.StartAsync();

#if ETCD
            try
            {
                if (etcd is EtcdClient)
                {
                    logger.LogInformation("Loading configuration from ETCD");

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    etcd.GetRangeValAsync($"{etcdPrefix}/Randomizer/", null, null, stoppingToken).ContinueWith(task =>
                    {
                        if (task.Exception != null)
                        {
                            logger.LogError(task.Exception, task.Exception.Message);
                            return;
                        }

                        var channelMatch = new Regex($@"^{Regex.Escape($"{etcdPrefix}/Randomizer/")}(?<channel>[\d]+)$");

                        foreach (var (Key, Value) in task.Result)
                        {
                            ulong channel;
                            bool randomize;

                            var match = channelMatch.Match(Key);

                            if (!match.Success)
                            {
                                continue;
                            }

                            if (!ulong.TryParse(match.Groups["channel"].Value, out channel))
                            {
                                continue;
                            }

                            if (!bool.TryParse(Value, out randomize))
                            {
                                continue;
                            }

                            logger.LogInformation($"Loading configuration from {Key}");

                            Randomize.AddOrUpdate(channel, randomize, (k, o) => randomize);
                        }
                    }, stoppingToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                }
            }
            catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
#endif

            while (!stoppingToken.WaitHandle.WaitOne(5000))
            {
                foreach (var channel in Randomize.Where(pair => pair.Value).Select(pair => pair.Key))
                {
                    if (random.Next(3600) == 1800)
                    {
                        await RandomImage(channel);
                    }
                }
            }
        }
    }
}
