using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly WebClient WebClient;
        private readonly Emoji Check;
        private readonly SHA1CryptoServiceProvider SHA1;
        private readonly Regex HasUrl;
        private Dictionary<ulong, RestUserMessage> History;
        private string[] Data;

        public Worker(ILogger<Worker> logger)
        {
            this.logger = logger;
            Command = Environment.GetEnvironmentVariable("Command");
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
                } else
                {
                    logger.LogError("UPLOADS directory does not exist: {uplodas}", new { uploads });
                }
            }

            random = new Random();
            History = new Dictionary<ulong, RestUserMessage>();
            CommandX = new Regex(@"\!" + Regex.Escape(Command) + @"[\s]*(.*)", RegexOptions.IgnoreCase);

            discord = new DiscordSocketClient(new DiscordSocketConfig
            {
                // Increases memory usage, but needed to get embeds that are parsed asynchonously and sent via MESSAGE_UPDATE event
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

            if (message.Content == "!purgeall")
            {
                var messages = message.Channel.GetMessagesAsync(limit: 20);
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

                return;
            }

            if (message.Content == "!purge")
            {
                if (History.ContainsKey(message.Author.Id))
                {
                    RestUserMessage item;
                    lock (History)
                    {
                        item = History[message.Author.Id];
                        History.Remove(message.Author.Id);
                    }
                    await item.DeleteAsync();
                }
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
                    } catch (Exception e)
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
                } else
                {
                    if (HasUrl.IsMatch(message.Content))
                    {
                        return;
                    }
                }
                
                if (message.Attachments.Count == 0 && message.Embeds.Count == 0)
                {
                    var index = random.Next(0, Data.Length - 1);
                    var file = Data[index];
                    RestUserMessage result = null;

                    switch (UriBase.Scheme)
                    {
                        case "file":
                            var info = new FileInfo(file);
                            result = await message.Channel.SendFileAsync(Data[index], info.Name);
                            break;
                        case "http":
                        case "https":
                            var request = WebRequest.Create(UriBase + file);
                            using (var response = await request.GetResponseAsync())
                            {
                                result = await message.Channel.SendFileAsync(response.GetResponseStream(), file);
                            }
                            break;
                    }

                    if (result != null)
                    {
                        History[message.Author.Id] = result;
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
            } catch (Exception e)
            {
                logger.LogError(e, e.Message);
            }
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

            while (!stoppingToken.WaitHandle.WaitOne(5000))
            {
            }
        }
    }
}
