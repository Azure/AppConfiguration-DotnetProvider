// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Options used when initially loading data into the configuration provider.
    /// </summary>
    public class StartupOptions
    {
        /// <summary>
        /// The maximum delay before timing out when loading data from Azure App Configuration on startup.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The maximum delay between retries when loading data from Azure App Configuration on startup.
        /// </summary>
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    }
}
