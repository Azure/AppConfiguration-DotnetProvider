// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;
using System.Threading;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Models
{
    internal class KeyValueWatcher
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
        public TimeSpan CacheExpirationInterval { get; set; }

        /// <summary>
        /// The cache expiration time for the key-value.
        /// </summary>
        public DateTimeOffset CacheExpires { get; set; }

        /// <summary>
        /// Semaphore that can be used to prevent simultaneous refresh of the key-value from multiple threads.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1);

        public override bool Equals(object obj)
        {
            if (obj is KeyValueWatcher kvWatcher)
            {
                return string.Equals(Key, kvWatcher.Key, StringComparison.Ordinal)
                    && string.Equals(Label, kvWatcher.Label, StringComparison.Ordinal);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Label != null ? Key.GetHashCode() ^ Label.GetHashCode() : Key.GetHashCode();
        }
    }
}
