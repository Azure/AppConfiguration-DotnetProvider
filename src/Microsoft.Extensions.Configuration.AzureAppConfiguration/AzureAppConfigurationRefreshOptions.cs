// Copyright (c) Microsoft Corporation.
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
        internal static readonly TimeSpan DefaultCacheExpirationInterval = TimeSpan.FromSeconds(30);
        internal static readonly TimeSpan MinimumCacheExpirationInterval = TimeSpan.FromMilliseconds(1000);

        internal TimeSpan CacheExpirationInterval { get; private set; } = DefaultCacheExpirationInterval;
        internal ISet<KeyValueWatcher> RefreshRegistrations = new HashSet<KeyValueWatcher>();
        
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
            RefreshRegistrations.Add(new KeyValueWatcher
            {
                Key = key,
                Label = label,
                RefreshAll = refreshAll
            });

            return this;
        }

        /// <summary>
        /// Sets the cache expiration time for the key-values registered for refresh. Default value is 30 seconds. Must be greater than 1 second.
        /// Any refresh operation triggered using <see cref="IConfigurationRefresher"/> will not update the value for a key until the cached value for that key has expired.
        /// </summary>
        /// <param name="cacheExpiration">Minimum time that must elapse before the cache is expired.</param>
        public AzureAppConfigurationRefreshOptions SetCacheExpiration(TimeSpan cacheExpiration)
        {
            if (cacheExpiration < MinimumCacheExpirationInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(cacheExpiration), cacheExpiration.TotalMilliseconds,
                    string.Format(ErrorMessages.CacheExpirationTimeTooShort, MinimumCacheExpirationInterval.TotalMilliseconds));
            }

            CacheExpirationInterval = cacheExpiration;
            return this;
        }
    }
}
