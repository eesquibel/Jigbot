using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;

namespace Jigbot.States
{
    public class ImagesState : IReadOnlyDictionary<int, string[]>
    {
        private string[][] Data;
        public readonly Uri[] UriBase;

        public int Count => Data.Length;

        public ImagesState(ILogger logger)
        {
            UriBase = Environment.GetEnvironmentVariable("URIBASE").Split(";").Select(uri =>
            {
                return new Uri(uri);
            }).ToArray();

            Data = new string[UriBase.Length][];

            for (var index = 0; index < UriBase.Length; index++)
            {
                var uri = UriBase[index];

                switch (uri.Scheme)
                {
                    case "file":
                        if (!Directory.Exists(uri.LocalPath))
                        {
                            logger.LogError("Directoy does not exist: {uri}", new { uri });
                            return;
                        }

                        Data[index] = Directory.GetFiles(uri.LocalPath, "*.*", SearchOption.TopDirectoryOnly);

                        break;
                    case "http":
                    case "https":
                        var request = new HttpClient();
                        using (var response = request.Send(new HttpRequestMessage(HttpMethod.Get, uri)))
                        {
                            using (var reader = new StreamReader(response.Content.ReadAsStream()))
                            {
                                using (var json = new JsonTextReader(reader))
                                {
                                    var data = (JArray)JToken.ReadFrom(json);
                                    Data[index] = data.Select(item => item.Value<string>("name")).ToArray();
                                }
                            }
                        }

                        break;
                }

                logger.LogInformation($"Found {Data[index].Length} images at {uri}");
            }

        }

        public IEnumerable<int> Keys => Enumerable.Range(0, Data.Length - 1);

        public IEnumerable<string[]> Values => Data;

        public bool ContainsKey(int key)
        {
            return key >= 0 && key <= Data.Length;
        }

        public bool TryGetValue(int key, [MaybeNullWhen(false)] out string[] value)
        {
            if (key >= 0 && key <= Data.Length)
            {
                if (Data[key] is string[])
                {
                    value = Data[key];
                    return true;
                }
            }

            value = null;
            return false;
        }

        public IEnumerator<KeyValuePair<int, string[]>> GetEnumerator()
        {
            return Data.Select((value, index) => KeyValuePair.Create(index, value)).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return Data.GetEnumerator();
        }

        public string[] this[int index]
        {
            get => Data[index];
        }
    }
}
