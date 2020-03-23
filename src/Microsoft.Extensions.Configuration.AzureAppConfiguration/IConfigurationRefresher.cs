// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using System;
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
        /// <exception cref="KeyVaultReferenceException">An error occurred when resolving a reference to an Azure Key Vault resource.</exception>
        /// <exception cref="RequestFailedException">The request failed with an error code from the server.</exception>
        /// <exception cref="AggregateException">
        /// The refresh operation failed with one or more errors. Check <see cref="AggregateException.InnerExceptions"/> for more details.
        /// </exception>
        /// <exception cref="InvalidOperationException">The refresh operation was invoked before Azure App Configuration Provider was initialized.</exception>
        /// <exception>An Azure.Identity.AuthenticationFailedException is thrown if authentication using TokenCredential passed to <see cref="AzureAppConfigurationKeyVaultOptions.SetCredential"/> method failed.</exception>
        Task RefreshAsync();

        /// <summary>
        /// Refreshes the data from the configuration store asynchronously. A return value indicates whether the operation succeeded.
        /// </summary>
        Task<bool> TryRefreshAsync();
    }
}
