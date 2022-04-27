// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants
{
    internal class FailOverConstants
    {
        // Timeouts to retry requests to config stores and their replicas after failure.
        public static readonly TimeSpan MinBackoffInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxBackoffInterval = TimeSpan.FromMinutes(10);
    }
}
