// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Azure.Data.AppConfiguration;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Defines the necessary interface to process configuration settings into key value pairs
    /// </summary>
    public interface IKeyValueAdapter
    {
        /// <summary>
        /// Processed settings into key value pairs
        /// </summary>
        /// <param name="setting">The <see cref="ConfigurationSetting"/> to be processed</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The processed settings</returns>
        Task<IEnumerable<KeyValuePair<string, string>>> ProcessKeyValue(ConfigurationSetting setting, CancellationToken cancellationToken);

        /// <summary>
        /// Whether the setting can be processed by the adapter
        /// </summary>
        /// <param name="setting">The <see cref="ConfigurationSetting"/> in question</param>
        /// <returns>A flag</returns>
        bool CanProcess(ConfigurationSetting setting);

        void InvalidateCache(ConfigurationSetting setting = null);

        bool NeedsRefresh();
    }
}
