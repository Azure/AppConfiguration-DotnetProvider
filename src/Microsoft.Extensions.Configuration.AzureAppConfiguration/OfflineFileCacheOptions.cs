// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options for controlling the behavior of an <see cref="OfflineFileCache"/>.
    /// </summary>
    [Obsolete("OfflineFileCacheOptions will be deprecated in a future release.")]
    public class OfflineFileCacheOptions
    {
        /// <summary>
        /// The file path to use for persisting cached data.
        /// </summary>
        public string Path { get; set; }
        
        /// <summary>
        /// Key used for encrypting data stored in the offline file cache.
        /// </summary>
        public byte[] Key { get; set; }

        /// <summary>
        /// Initilization vector used for encryption within the offline file cache.
        /// </summary>
        public byte[] IV { get; set; }

        /// <summary>
        /// Key used for signing data stored in the offline file cache.
        /// </summary>
        public byte[] SignKey { get; set; }
    }
}
