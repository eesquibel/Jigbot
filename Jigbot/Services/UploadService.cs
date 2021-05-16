using Discord;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Jigbot.Services
{
    public class UploadService
    {
        private readonly string[] Uploads;
        private readonly WebClient WebClient;
        private readonly SHA1CryptoServiceProvider SHA1;
        private static readonly Emoji Check = new Emoji("\u2611\uFE0F");

        public readonly bool Enabled = false;

        public ILogger logger { get; set; }

        public UploadService(ILogger logger)
        {
            this.logger = logger;
            var uploads = Environment.GetEnvironmentVariable("UPLOADS");
            var list = new SortedList<int, string>();
            var i = 0;

            if (!string.IsNullOrEmpty(uploads))
            {
                foreach (var dir in uploads.Split(";"))
                {
                    if (Directory.Exists(dir))
                    {
                        list.Add(i++, (new DirectoryInfo(dir)).FullName);
                    } else
                    {
                        try
                        {
                            var info = Directory.CreateDirectory(dir);
                            list.Add(i++, info.FullName);
                        }
                        catch (Exception)
                        {
                            logger.LogError("UPLOADS directory does not exist: {dir}", new { dir });
                        }
                    }
                }
                
                if (list.Count > 0)
                {
                    Uploads = list.Values.ToArray();
                    WebClient = new WebClient();
                    SHA1 = new SHA1CryptoServiceProvider();
                    Enabled = true;
                }
            }
        }

        public async Task GetEmbeds(IMessage message, int index)
        {
            if (!Enabled)
            {
                return;
            }

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
                        tasks[i++] = Get(uri, index);
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

        public Task Get(IAttachment attachment, int index)
        {
            return Get(new Uri(attachment.Url), index);
        }

        public Task Get(string uri, int index)
        {
            return Get(new Uri(uri), index);
        }

        public async Task Get(Uri uri, int index)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                var bytes = await WebClient.DownloadDataTaskAsync(uri);

                logger.LogInformation($"Downloaded {uri} ({bytes.Length} bytes)");

                var filename = Uploads[index] + "/";
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
    }
}
