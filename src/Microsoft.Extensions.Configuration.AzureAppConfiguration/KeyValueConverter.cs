using Azure.Data.AppConfiguration;
using System.Text.Json;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class KeyValueConverter : JsonSerializerOptions
    {
        public override ConfigurationSetting Create(Type _)
        {
            return new ConfigurationSetting(null, null);
        }
    }
}
