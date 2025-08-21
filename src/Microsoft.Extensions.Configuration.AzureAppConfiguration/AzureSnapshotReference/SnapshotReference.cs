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

        /// <param name="snapshotName">The name of the snapshot.</param>
        public SnapshotReference(string snapshotName)
        {
            SnapshotName = snapshotName;
        }
    }
}