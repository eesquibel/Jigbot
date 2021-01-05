using Discord;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Jigbot.Services
{
    public class UploadService
    {
        private readonly string Uploads;
        private readonly WebClient WebClient;
        private readonly SHA1CryptoServiceProvider SHA1;
        private static readonly Emoji Check = new Emoji("\u2611\uFE0F");

        public readonly bool Enabled = false;

        public ILogger logger { get; set; }

        public UploadService(ILogger logger)
        {
            this.logger = logger;
            var uploads = Environment.GetEnvironmentVariable("UPLOADS");

            if (!string.IsNullOrEmpty(uploads))
            {
                if (Directory.Exists(uploads))
                {
                    Uploads = (new DirectoryInfo(uploads)).FullName;
                    WebClient = new WebClient();
                    SHA1 = new SHA1CryptoServiceProvider();
                    Enabled = true;
                }
                else
                {
                    logger.LogError("UPLOADS directory does not exist: {uplodas}", new { uploads });
                }
            }
        }

        public async Task GetEmbeds(IMessage message)
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
                        tasks[i++] = Get(uri);
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

        public Task Get(IAttachment attachment)
        {
            return Get(new Uri(attachment.Url));
        }

        public Task Get(string uri)
        {
            return Get(new Uri(uri));
        }

        public async Task Get(Uri uri)
        {
            if (!Enabled)
            {
                return;
            }

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
    }
}
