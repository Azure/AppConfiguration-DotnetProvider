using Azure.Data.AppConfiguration;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ConfigurationClientFactory
    {
        public static ConfigurationClient CreateConfigurationClient(string connectionString)
        {
            var clientOptions = AzureAppConfigurationProvider.GetClientOptions();
            return new ConfigurationClient(connectionString, clientOptions); 
        }
    }
}
