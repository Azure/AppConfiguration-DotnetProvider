namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    public interface IOfflineCache
    {
        string Import(AzureAppConfigurationOptions options);
        void Export(AzureAppConfigurationOptions options, string data);
    }
}
