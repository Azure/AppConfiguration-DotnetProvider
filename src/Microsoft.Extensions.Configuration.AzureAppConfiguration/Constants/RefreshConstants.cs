// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class RefreshConstants
    {
        // Key-values
        public static readonly TimeSpan DefaultCacheExpirationInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinimumCacheExpirationInterval = TimeSpan.FromMilliseconds(1000);

        // Feature flags
        public static readonly TimeSpan DefaultFeatureFlagsCacheExpirationInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinimumFeatureFlagsCacheExpirationInterval = TimeSpan.FromMilliseconds(1000);

        // Key Vault secrets
        public static readonly TimeSpan MinimumSecretRefreshInterval = TimeSpan.FromMilliseconds(1000);

        // Backoff during refresh failures
        public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(10);
    }
}
