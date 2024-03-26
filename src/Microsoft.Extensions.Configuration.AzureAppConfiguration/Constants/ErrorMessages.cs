// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class ErrorMessages
    {
        public const string CacheExpirationTimeTooShort = "The cache expiration time cannot be less than {0} milliseconds.";
        public const string SecretRefreshIntervalTooShort = "The secret refresh interval cannot be less than {0} milliseconds.";
        public const string FeatureFlagInvalidJsonProperty = "Invalid property '{0}' for feature flag '{1}'. Could not be converted to json type: '{2}'.";
    }
}
