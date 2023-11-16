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
        /// The amount of time allowed to load data from Azure App Configuration on startup.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);
    }
}
