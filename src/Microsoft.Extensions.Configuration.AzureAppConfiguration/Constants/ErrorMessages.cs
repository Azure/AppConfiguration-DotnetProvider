﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ErrorMessages
    {
        public const string RefreshIntervalTooShort = "The refresh interval cannot be less than {0} milliseconds.";
        public const string SecretRefreshIntervalTooShort = "The secret refresh interval cannot be less than {0} milliseconds.";
        public const string FeatureFlagInvalidJsonProperty = "Invalid property '{0}' for feature flag. Key: '{1}'. Found type: '{2}'. Expected type: '{3}'.";
        public const string FeatureFlagInvalidFormat = "Invalid json format for feature flag. Key: '{0}'.";
        public const string InvalidKeyVaultReference = "Invalid Key Vault reference.";
    }
}
