﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using System;
using System.Collections.Generic;

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
        /// Tags of the key-value to be watched.
        /// </summary>
        public IEnumerable<string> Tags { get; set; }

        /// <summary>
        /// A flag to refresh all key-values.
        /// </summary>
        public bool RefreshAll { get; set; }

        /// <summary>
        /// The minimum time that must elapse before the key-value is refreshed.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; }

        /// <summary>
        /// The next time when this key-value can be refreshed.
        /// </summary>
        public DateTimeOffset NextRefreshTime { get; set; }

        public override bool Equals(object obj)
        {
            if (obj is KeyValueWatcher kvWatcher)
            {
                return Key == kvWatcher.Key && Label.NormalizeNull() == kvWatcher.Label.NormalizeNull();
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Label != null ? Key.GetHashCode() ^ Label.GetHashCode() : Key.GetHashCode();
        }
    }
}
