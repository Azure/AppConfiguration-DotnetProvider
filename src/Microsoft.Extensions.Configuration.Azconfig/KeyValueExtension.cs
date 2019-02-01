using System;
using System.Collections.Generic;
using Microsoft.Azconfig.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Extensions.Configuration.Azconfig
{
    internal static class KeyValueExtension
    {
        private class KeyValueConverter : CustomCreationConverter<IKeyValue>
        {
            public override IKeyValue Create(Type _)
            {
                return new KeyValue(null);
            }
        }

        public static string ToJsonString(this IDictionary<string, IKeyValue> data)
        {
            return JsonConvert.SerializeObject(data);
        }

        public static IDictionary<string, IKeyValue> ToKeyValues(this string data)
        {
            return JsonConvert.DeserializeObject<IDictionary<string, IKeyValue>>(data, new KeyValueConverter());
        }
    }
}
