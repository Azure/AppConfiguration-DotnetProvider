using System;
using System.Collections.Generic;
using Microsoft.Azconfig.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    public abstract class OfflineCache
    {
        protected OfflineCacheOptions Options { set; get; }

        private class KeyValueConverter : CustomCreationConverter<IKeyValue>
        {
            public override IKeyValue Create(Type _)
            {
                return new KeyValue(null);
            }
        }

        public abstract string Import();
        public abstract void Export(string data);

        internal void SetData(IDictionary<string, IKeyValue> data)
        {
            Export(JsonConvert.SerializeObject(data));
        }

        internal IDictionary<string, IKeyValue> GetData()
        {
            return JsonConvert.DeserializeObject<IDictionary<string, IKeyValue>>(Import(), new KeyValueConverter());
        }
    }
}
