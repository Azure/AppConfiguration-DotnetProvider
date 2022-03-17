// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using Azure;
using Azure.Data.AppConfiguration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal interface IConfigurationClient
    {
        /// <summary>
        /// Retrieve an existing <see cref="ConfigurationSetting"/>, uniquely
        /// identified by key and label, from the configuration store.
        /// </summary>
        /// <param name="key">The primary identifier of the configuration setting to retrieve.</param>
        /// <param name="label">A label used to group this configuration setting with others.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> controlling the request lifetime.</param>
        /// <returns>A response containing the retrieved <see cref="ConfigurationSetting"/>.</returns>
        Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(string key, string label = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieve an existing <see cref="ConfigurationSetting"/> from the configuration store.
        /// </summary>
        /// <param name="setting">The <see cref="ConfigurationSetting" /> to retrieve.</param>
        /// <param name="onlyIfChanged">
        /// If set to true, only retrieve the setting from the configuration store if it
        /// has changed since the client last retrieved it. It is determined to have changed
        /// if the ETag field on the passed-in Azure.Data.AppConfiguration.ConfigurationSetting
        /// is different from the ETag of the setting in the configuration store. If it has
        /// not changed, the returned response will have have no value, and will throw if
        /// response.Value is accessed. Callers may check the status code on the response
        /// to avoid triggering the exception.
        /// </param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> controlling the request lifetime.</param>
        /// <returns>A response containing the retrieved <see cref="ConfigurationSetting"/>.</returns>
        Task<Response<ConfigurationSetting>> GetConfigurationSettingAsync(ConfigurationSetting setting, bool onlyIfChanged = false, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves one or more <see cref="ConfigurationSetting"/> entities
        /// that match the options specified in the passed-in <paramref name="selector"/>.
        /// </summary>
        /// <param name="selector">Options used to select a set of <see cref="ConfigurationSetting"/> entities from the configuration store.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> controlling the request lifetime.</param>
        /// <returns>An enumerable collection containing the retrieved <see cref="ConfigurationSetting"/> entities.</returns>
        Task<IEnumerable<ConfigurationSetting>> GetConfigurationSettingsAsync(SettingSelector selector, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds an external synchronization token to ensure service requests receive up-to-date values
        /// </summary>
        /// <param name="endpoint">The endpoint to update the synchronization token for.</param>
        /// <param name="token">The synchronization token value.</param>
        void UpdateSyncToken(Uri endpoint, string token);
    }
}