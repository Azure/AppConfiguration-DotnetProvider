using System;
using Microsoft.Azure.AppConfiguration.Azconfig;
using Newtonsoft.Json.Converters;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class KeyValueConverter : CustomCreationConverter<IKeyValue>
    {
        public override IKeyValue Create(Type _)
        {
            return new KeyValue(null);
        }
    }
}
