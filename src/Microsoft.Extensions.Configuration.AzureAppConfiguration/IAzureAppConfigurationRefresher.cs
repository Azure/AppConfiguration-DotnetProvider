using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An interface used to refresh the data being watched by Azure App Configuration.
    /// </summary>
    public interface IAzureAppConfigurationRefresher
    {
        /// <summary>
        /// Refreshes the data from the configuration store asynchronously.
        /// </summary>
        Task Refresh();
    }
}
