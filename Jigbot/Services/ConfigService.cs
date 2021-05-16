using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Threading;
using Discord;
#if ETCD
using dotnet_etcd;
#endif

namespace Jigbot.Services
{

    public class ConfigService
    {
        public ILogger logger { get; set; }

        public readonly string[] Command;

#if ETCD
        private readonly string etcdPrefix;
        private readonly EtcdClient etcd;
#endif

        public ConfigService(ILogger logger)
        {
            this.logger = logger;

            Command = Environment.GetEnvironmentVariable("COMMAND")?.ToLower().Split(";");
#if ETCD
            logger.LogInformation("ETCD support is compiled");

            etcdPrefix = Environment.GetEnvironmentVariable("ETCD_PREFIX");
            var etcdUrl = Environment.GetEnvironmentVariable("ETCD_URL");
            if (string.IsNullOrEmpty(etcdUrl))
            {
                logger.LogInformation("ETCD is disabled");
            }
            else
            {
                try
                {
                    logger.LogInformation($"ETCD is using {etcdUrl}");
                    etcd = new EtcdClient(etcdUrl);
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
#endif
        }

        public int GetIndex(string cmd)
        {
            return Array.IndexOf(Command, cmd);
        }

        public int GetIndex(IMessageChannel channel)
        {
            return 0;
        }

        public void Put(string key, object value)
        {
#if ETCD
            if (etcd is EtcdClient)
            {
                try
                {
                    etcd.Put($"{etcdPrefix}/{key}", value.ToString());
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
#else
            return;
#endif
        }

        public async Task PutAsync(string key, object value)
        {
#if ETCD
            if (etcd is EtcdClient)
            {
                try
                {
                    await etcd.PutAsync($"{etcdPrefix}/{key}", value.ToString());
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }
#else
            return;
#endif
        }

        public async Task<IDictionary<string, string>> GetRange(string prefix)
        {
#if ETCD
            if (etcd is EtcdClient)
            {
                int backoff = 2000;

                retry:
                try
                {
                    var results = await etcd.GetRangeValAsync($"{etcdPrefix}/{prefix}/");
                    return results;
                }
                catch (Grpc.Core.RpcException e)
                {
                    if (e.StatusCode == Grpc.Core.StatusCode.Unavailable)
                    {
                        Thread.Sleep(backoff);

                        if (backoff < 60000)
                        {
                            backoff += 2000;
                        }

                        goto retry;
                    }
                }
            }
#endif
            return await Task.FromResult(new Dictionary<string, string>(0) as IDictionary<string, string>);
        }
    }
}
