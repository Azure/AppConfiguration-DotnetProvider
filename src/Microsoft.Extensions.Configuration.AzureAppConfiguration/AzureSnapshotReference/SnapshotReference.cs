using Azure.Data.AppConfiguration;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    /// <summary>
    /// Represents a reference to a configuration snapshot that needs to be resolved.
    /// </summary>
    internal class SnapshotReference
    {
        public string SnapshotName { get; set; }

        public ConfigurationClient Client { get; set; }

        public CancellationToken CancellationToken { get; set; }

        /// <param name="snapshotName">The name of the snapshot.</param>
        /// <param name="client">The configuration client.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public SnapshotReference(string snapshotName, ConfigurationClient client, CancellationToken cancellationToken)
        {
            SnapshotName = snapshotName;
            Client = client;
            CancellationToken = cancellationToken;
        }
    }
}