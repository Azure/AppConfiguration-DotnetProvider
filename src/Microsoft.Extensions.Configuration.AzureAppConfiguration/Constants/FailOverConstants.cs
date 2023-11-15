// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class FailOverConstants
    {
        // Timeouts to retry requests to config stores and their replicas after failure.
        public static readonly TimeSpan MinBackoffDuration = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxBackoffDuration = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan StartupFixedBackoffDuration = TimeSpan.FromMinutes(10);
        public static readonly TimeSpan MaxFixedStartupBackoff = TimeSpan.FromSeconds(30);
    }
}
