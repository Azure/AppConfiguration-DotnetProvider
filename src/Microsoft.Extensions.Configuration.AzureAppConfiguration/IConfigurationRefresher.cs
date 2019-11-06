﻿using System;
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
        /// <exception cref="UnauthorizedAccessException">The caller is not authorized to perform this operation.</exception>
        /// <exception cref="InvalidOperationException">The initial configuration is not loaded or the caller does not have the required permission to perform this operation.</exception>
        /// <exception cref="RefreshFailedException">The refresh operation failed.</exception>
        Task Refresh();
    }
}
