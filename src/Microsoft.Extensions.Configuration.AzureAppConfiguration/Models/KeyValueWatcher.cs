using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    class KeyValueWatcher
    {
        /// <summary>
        /// Key of the key-value to be watched.
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Label of the key-value to be watched.
        /// </summary>
        public string Label { get; set; }

        /// <summary>
        /// A flag to refresh all key-values.
        /// </summary>
        public bool RefreshAll { get; set; }

        /// <summary>
        /// The minimum time that must elapse before the key-value is refreshed.
        /// </summary>
        public TimeSpan CacheExpirationTime { get; set; }
    }
}
