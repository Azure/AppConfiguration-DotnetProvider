﻿// Copyright (c) Microsoft Corporation.
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
        public TimeSpan CacheExpirationTime { get; set; }

        /// <summary>
        /// The most-recent time when the key-value was refreshed.
        /// </summary>
        public DateTimeOffset LastRefreshTime { get; set; }

        /// <summary>
        /// Semaphore that can be used to prevent simultaneous refresh of the key-value from multiple threads.
        /// </summary>
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1);
    }
}
