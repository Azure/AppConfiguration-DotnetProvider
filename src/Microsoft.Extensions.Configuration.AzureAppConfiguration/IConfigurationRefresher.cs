// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using System;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// An interface used to trigger an update for the data registered for refresh with App Configuration.
    /// </summary>
    public interface IConfigurationRefresher
    {
        /// <summary>
        /// The App Configuration endpoint.
        /// </summary>
        Uri AppConfigurationEndpoint { get; }

        /// <summary>
        /// Refreshes the data from App Configuration asynchronously.
        /// </summary>
        /// <exception cref="KeyVaultReferenceException">An error occurred when resolving a reference to an Azure Key Vault resource.</exception>
        /// <exception cref="RequestFailedException">The request failed with an error code from the server.</exception>
        /// <exception cref="AggregateException">
        /// The refresh operation failed with one or more errors. Check <see cref="AggregateException.InnerExceptions"/> for more details.
        /// </exception>
        /// <exception cref="InvalidOperationException">The refresh operation was invoked before Azure App Configuration Provider was initialized.</exception>
        Task RefreshAsync();

        /// <summary>
        /// Refreshes the data from App Configuration asynchronously. A return value indicates whether the operation succeeded.
        /// </summary>
        Task<bool> TryRefreshAsync();

        /// <summary>
        /// Sets the cached value for key-values registered for refresh as dirty.
        /// A random delay is added before the cached value is marked as dirty to reduce potential throttling in case multiple instances refresh at the same time.
        /// </summary>
        /// <param name="maxDelay">Maximum delay before the cached value is marked as dirty. Default value is 30 seconds.</param>
        void SetDirty(TimeSpan? maxDelay = null);
    }
}
