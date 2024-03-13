// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
using System;

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class RefreshConstants
    {
        // Key-values
        public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(1);

        // Feature flags
        public static readonly TimeSpan DefaultFeatureFlagsRefreshInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MinimumFeatureFlagsRefreshInterval = TimeSpan.FromSeconds(1);

        // Key Vault secrets
        public static readonly TimeSpan MinimumSecretRefreshInterval = TimeSpan.FromSeconds(1);

        // Backoff during refresh failures
        public static readonly TimeSpan DefaultMinBackoff = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(10);
    }
}
