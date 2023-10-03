﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
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
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <exception cref="KeyVaultReferenceException">An error occurred when resolving a reference to an Azure Key Vault resource.</exception>
        /// <exception cref="RequestFailedException">The request failed with an error code from the server.</exception>
        /// <exception cref="AggregateException">
        /// The refresh operation failed with one or more errors. Check <see cref="AggregateException.InnerExceptions"/> for more details.
        /// </exception>
        /// <exception cref="InvalidOperationException">The refresh operation was invoked before Azure App Configuration Provider was initialized.</exception>
        Task RefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the data from App Configuration asynchronously. A return value indicates whether the operation succeeded.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Process the details of a <see cref="PushNotification"/> object to ensure the latest key-values are provided in 
        /// the next request to App Configuration. The next request will be made after the cached values have been marked as dirty.
        /// </summary>
        /// <param name="pushNotification">Fully populated <see cref="PushNotification"/> object.</param>
        /// /// <param name="maxDelay">Maximum delay before the cached value is marked as dirty. Default value is 30 seconds.</param>
        void ProcessPushNotification(PushNotification pushNotification, TimeSpan? maxDelay = null);

        /// <summary>
        /// Process the details of a <see cref="KeyValuePushNotification"/> object to ensure the latest key-values are provided in 
        /// the next request to App Configuration. The next request will be made after the cached values have been marked as dirty.
        /// </summary>
        /// <param name="keyValuePushNotification">Fully populated <see cref="KeyValuePushNotification"/> object.</param>
        /// /// <param name="maxDelay">Maximum delay before the cached value is marked as dirty. Default value is 30 seconds.</param>
        void ProcessKeyValuePushNotification(KeyValuePushNotification keyValuePushNotification, TimeSpan? maxDelay = null);
    }
}
