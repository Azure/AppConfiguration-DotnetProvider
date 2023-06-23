// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration.Constants
{
    /// <summary>
    /// Constants used to conditionally add the Azure App Configuration provider.
    /// </summary>
    public class ConditionalProviderConstants
    {
        /// <summary>
        /// Environment variable used to disable the Azure App Configuration provider.
        /// </summary>
        public const string DisableProviderEnvironmentVariable = "AZURE_APP_CONFIGURATION_PROVIDER_DISABLED";
    }
}
