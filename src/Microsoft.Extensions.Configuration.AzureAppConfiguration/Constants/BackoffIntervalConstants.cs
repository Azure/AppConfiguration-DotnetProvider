﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants
{
    internal class BackoffIntervalConstants
    {
        // Timeouts to retry requests to config stores and their replicas after failure.
        public static readonly TimeSpan MinBackoffInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxBackoffInterval = TimeSpan.FromMinutes(10);

        // Interval constants related to initiating a parallel request to the replica when one doesn't respond.
        public static readonly TimeSpan MinParallelRetryInterval = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan DefaultParallelRetryInterval = TimeSpan.FromSeconds(30);
    }
}