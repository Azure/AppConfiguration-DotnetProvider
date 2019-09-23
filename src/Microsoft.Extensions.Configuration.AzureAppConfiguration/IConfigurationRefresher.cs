using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An interface used to trigger an update for the data registered for refresh with the configuration store.
    /// </summary>
    public interface IConfigurationRefresher
    {
        /// <summary>
        /// Refreshes the data from the configuration store asynchronously.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task Refresh(CancellationToken cancellationToken = default);
    }
}
