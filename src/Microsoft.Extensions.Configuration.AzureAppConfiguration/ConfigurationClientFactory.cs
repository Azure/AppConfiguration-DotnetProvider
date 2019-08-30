using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory
    {
        internal static ConfigurationClient GetConfigurationClient(string connectionString)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();
            return new ConfigurationClient(connectionString, clientOptions); 
        }
    }
}
