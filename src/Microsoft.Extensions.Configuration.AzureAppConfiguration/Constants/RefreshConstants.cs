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
        public static readonly TimeSpan MinimumCacheExpirationInterval = TimeSpan.FromSeconds(1);

        // Feature flags
        public static readonly TimeSpan DefaultFeatureFlagsCacheExpirationInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinimumFeatureFlagsCacheExpirationInterval = TimeSpan.FromSeconds(1);

        // Key Vault secrets
        public static readonly TimeSpan MinimumSecretRefreshInterval = TimeSpan.FromSeconds(1);

        // Backoff during refresh failures
        public static readonly TimeSpan DefaultMinBackoff = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(10);

        // Event Types
        public static readonly string KeyValueModified = "Microsoft.AppConfiguration.KeyValueModified";
        public static readonly string KeyValueDeleted = "Microsoft.AppConfiguration.KeyValueDeleted";
    }
}
