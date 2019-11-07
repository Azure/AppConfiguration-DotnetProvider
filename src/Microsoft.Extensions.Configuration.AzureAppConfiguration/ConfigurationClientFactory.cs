using Azure.Core;
using Azure.Data.AppConfiguration;
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory
    {
        public static ConfigurationClient CreateConfigurationClient(string connectionString)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();
            return new ConfigurationClient(connectionString, clientOptions); 
        }

        public static ConfigurationClient CreateConfigurationClient(Uri hostUri, TokenCredential credential)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();

            // TODO : Update this code before merge once AAD support is available from SDK
            var connectionString = Environment.GetEnvironmentVariable("connection_string");
            return new ConfigurationClient(connectionString, clientOptions);
        }
    }
}
