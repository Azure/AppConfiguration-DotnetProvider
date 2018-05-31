namespace Tests.AppConfig
{
    using Microsoft.Extensions.Configuration.AppConfig;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    class AppConfigClient : IAppConfigClient
    {
        private Dictionary<string, IKeyValue> _data = new Dictionary<string, IKeyValue>();

        public Dictionary<string, IKeyValue> Data {
            get {
                return _data;
            }
        }

        public Task<IEnumerable<IKeyValue>> GetSettings(string prefix)
        {
            var settings = new List<IKeyValue>();

            foreach (var kvp in Data)
            {
                settings.Add(kvp.Value);
            }

            return Task.FromResult((IEnumerable<IKeyValue>)(Data.Values.Select(kv => KeyValue.Clone(kv))));
        }

        public Task<IKeyValue> GetSetting(string key)
        {
            return Task.FromResult(Data.ContainsKey(key) ? (IKeyValue)KeyValue.Clone(Data[key]) : null);
        }

        public async Task<string> GetETag(string key)
        {
            IKeyValue setting = await GetSetting(key);

            return setting?.ETag;
        }
    }
}
