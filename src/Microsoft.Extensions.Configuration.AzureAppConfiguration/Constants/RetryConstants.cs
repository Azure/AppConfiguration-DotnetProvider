// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants
{
    internal class RetryConstants
    {
        // Timeouts to retry requests to primary config stores after HTTP status codes 503 or 429.
        public static readonly TimeSpan DefaultMinRetryAfter = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultMaxRetryAfter = TimeSpan.FromMinutes(10);
    }
}
