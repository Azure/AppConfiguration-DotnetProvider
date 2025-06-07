// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    internal class HealthCheckConstants
    {
        public const string HealthCheckRegistrationName = "Microsoft.Extensions.Configuration.AzureAppConfiguration";
        public const string NoProviderFoundMessage = "No configuration provider is found.";
        public const string LoadNotCompletedMessage = "The initial load is not completed.";
        public const string RefreshFailedMessage = "The last refresh attempt failed.";
    }
}
