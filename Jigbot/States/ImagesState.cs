using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Jigbot.States
{
    public class ImagesState : IReadOnlyCollection<string>
    {
        private string[] Data;
        public readonly Uri UriBase;

        public string Scheme => UriBase.Scheme;

        public int Count => Data.Length;

        public ImagesState(ILogger logger)
        {
            UriBase = new Uri(Environment.GetEnvironmentVariable("URIBASE"));

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
                    using (var response = request.GetResponse())
                    {
                        using (var reader = new StreamReader(response.GetResponseStream()))
                        {
                            using (var json = new JsonTextReader(reader))
                            {
                                var data = (JArray)JToken.ReadFrom(json);
                                Data = data.Select(item => item.Value<string>("name")).ToArray();
                            }
                        }
                    }

                    break;
            }

            logger.LogInformation($"Found {Data.Length} images");
        }

        public IEnumerator<string> GetEnumerator()
        {
            return Data.AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Data.GetEnumerator();
        }

        public string this[int index]
        {
            get => Data[index];
        }
    }
}
