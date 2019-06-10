using Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants;
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
        internal static readonly TimeSpan DefaultCacheExpirationTime = TimeSpan.FromSeconds(60);
        internal static readonly TimeSpan MinimumCacheExpirationTime = TimeSpan.FromMilliseconds(1000);

        internal TimeSpan CacheExpirationTime { get; private set; } = DefaultCacheExpirationTime;
        internal IDictionary<string, KeyValueWatcher> RefreshRegistrations = new Dictionary<string, KeyValueWatcher>();

        /// <summary>
        /// Register refresh for the specified key-value and refresh it if the value has changed.
        /// </summary>
        /// <param name="key">Key of the key-value.</param>
        /// <param name="refreshAll">If true, refreshes all key-values for the configuration provider if any property of the key-value has changed.</param>
        public AzureAppConfigurationRefreshOptions Register(string key, bool refreshAll)
        {
            return RegisterHelper(key, LabelFilter.Null, refreshAll);
        }

        /// <summary>
        /// Register refresh for the specified key-value and refresh it if the value has changed.
        /// </summary>
        /// <param name="key">Key of the key-value.</param>
        /// <param name="label">Label of the key-value.</param>
        /// <param name="refreshAll">If true, refreshes all key-values for the configuration provider if any property of the key-value has changed.</param>
        public AzureAppConfigurationRefreshOptions Register(string key, string label = LabelFilter.Null, bool refreshAll = false)
        {
            return RegisterHelper(key, label, refreshAll);
        }

        /// <summary>
        /// Sets the cache expiration time for the key-values registered for refresh. Must be greater than 1000 ms.
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

        private AzureAppConfigurationRefreshOptions RegisterHelper(string key, string label, bool refreshAll)
        {
            RefreshRegistrations[key] = new KeyValueWatcher
            {
                Key = key,
                Label = label,
                RefreshAll = refreshAll
            };

            return this;
        }
    }
}
