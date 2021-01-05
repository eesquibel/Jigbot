using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if ETCD
using dotnet_etcd;
using Microsoft.Extensions.Logging;
#endif

namespace Jigbot.Services
{

    public class ConfigService
    {
        public ILogger logger { get; set; }

        public readonly string Command;

#if ETCD
        private readonly string etcdPrefix;
        private readonly EtcdClient etcd;
#endif

        public ConfigService(ILogger logger)
        {
            this.logger = logger;
            Command = Environment.GetEnvironmentVariable("COMMAND")?.ToLower();
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

        public Task<IDictionary<string, string>> GetRange(string prefix)
        {
#if ETCD
            return etcd.GetRangeValAsync($"{etcdPrefix}/{prefix}/");
#else
            return Task.FromResult(new Dictionary<string, string>(0) as IDictionary<string, string>);
#endif
        }
    }
}
