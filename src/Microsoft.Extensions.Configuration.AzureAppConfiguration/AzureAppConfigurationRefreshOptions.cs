﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Models;
using System;
using System.Collections.Generic;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used to configure the refresh behavior of an Azure App Configuration provider.
    /// </summary>
    public class AzureAppConfigurationRefreshOptions
    {
        internal static readonly TimeSpan DefaultCacheExpirationTime = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan MinimumCacheExpirationTime = TimeSpan.FromMilliseconds(1000);

        internal TimeSpan CacheExpirationTime { get; private set; } = DefaultCacheExpirationTime;
        internal IDictionary<string, KeyValueWatcher> RefreshRegistrations = new Dictionary<string, KeyValueWatcher>();

        /// <summary>
        /// Register the specified key-value to be refreshed when the configuration provider's <see cref="IConfigurationRefresher"/> triggers a refresh.
        /// The <see cref="IConfigurationRefresher"/> instance can be obtained by calling <see cref="AzureAppConfigurationOptions.GetRefresher()"/>.
        /// </summary>
        /// <param name="key">Key of the key-value.</param>
        /// <param name="refreshAll">If true, a change in the value of this key refreshes all key-values being used by the configuration provider.</param>
        public AzureAppConfigurationRefreshOptions Register(string key, bool refreshAll)
        {
            return Register(key, LabelFilter.Null, refreshAll);
        }

        /// <summary>
        /// Register the specified key-value to be refreshed when the configuration provider's <see cref="IConfigurationRefresher"/> triggers a refresh.
        /// The <see cref="IConfigurationRefresher"/> instance can be obtained by calling <see cref="AzureAppConfigurationOptions.GetRefresher()"/>.
        /// </summary>
        /// <param name="key">Key of the key-value.</param>
        /// <param name="label">Label of the key-value.</param>
        /// <param name="refreshAll">If true, a change in the value of this key refreshes all key-values being used by the configuration provider.</param>
        public AzureAppConfigurationRefreshOptions Register(string key, string label = LabelFilter.Null, bool refreshAll = false)
        {
            RefreshRegistrations[key] = new KeyValueWatcher
            {
                Key = key,
                Label = label,
                RefreshAll = refreshAll
            };

            return this;
        }

        /// <summary>
        /// Sets the cache expiration time for the key-values registered for refresh. Default value is 30 seconds. Must be greater than 1 second.
        /// Any refresh operation triggered using <see cref="IConfigurationRefresher"/> will not update the value for a key until the cached value for that key has expired.
        /// </summary>
        /// <param name="cacheExpirationTime">Minimum time that must elapse before the cache is expired.</param>
        public AzureAppConfigurationRefreshOptions SetCacheExpiration(TimeSpan cacheExpirationTime)
        {
            if (cacheExpirationTime < MinimumCacheExpirationTime)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheExpirationTime), cacheExpirationTime.TotalMilliseconds,
                    string.Format(ErrorMessages.CacheExpirationTimeTooShort, MinimumCacheExpirationTime.TotalMilliseconds));
            }

            CacheExpirationTime = cacheExpirationTime;
            return this;
        }
    }
}
