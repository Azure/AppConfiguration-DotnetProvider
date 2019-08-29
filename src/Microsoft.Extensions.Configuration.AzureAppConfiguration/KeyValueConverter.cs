using Azure.Data.AppConfiguration;
using Newtonsoft.Json.Converters;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class KeyValueConverter : CustomCreationConverter<ConfigurationSetting>
    {
        public override ConfigurationSetting Create(Type _)
        {
            return new ConfigurationSetting(null, null);
        }
    }
}
