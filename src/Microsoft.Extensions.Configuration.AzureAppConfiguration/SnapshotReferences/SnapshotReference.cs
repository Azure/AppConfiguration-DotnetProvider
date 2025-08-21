// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.SnapshotReferences
{
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