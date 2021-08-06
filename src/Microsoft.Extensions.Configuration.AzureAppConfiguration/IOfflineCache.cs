// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Defines the necessary interface to perform offline caching of Azure App Configuration data.
    /// </summary>
    [Obsolete("Offline caching capabilities are being deprecated to reduce security vulnerabilities.")]
    public interface IOfflineCache
    {
        /// <summary>
        /// Import Azure App Configuration data according to the provided <see cref="AzureAppConfigurationOptions"/>.
        /// </summary>
        /// <param name="options">Options describing what data is being requested.</param>
        /// <returns>Cached Azure App Configuration data.</returns>
        [Obsolete("Offline caching capabilities are being deprecated to reduce security vulnerabilities.")]
        string Import(AzureAppConfigurationOptions options);

        /// <summary>
        /// Export Azure App Configuration data to an offline cache.
        /// </summary>
        /// <param name="options">The options that were used to retrieve the Azure App Configuration data.</param>
        /// <param name="data">The data to cache for later usage.</param>
        [Obsolete("Offline caching capabilities are being deprecated to reduce security vulnerabilities.")]
        void Export(AzureAppConfigurationOptions options, string data);
    }
}
