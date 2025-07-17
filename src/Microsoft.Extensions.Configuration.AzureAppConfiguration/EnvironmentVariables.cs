// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Environment variables used to configure Azure App Configuration provider behavior.
    /// </summary>
    internal static class EnvironmentVariables
    {
        /// <summary>
        /// Environment variable to disable Feature Management schema compatibility.
        /// The value of this variable is a boolean string, e.g. "true" or "false".
        /// When set to "true", schema compatibility checks for feature flags are disabled,
        /// and all feature flags will be interpreted using the Microsoft Feature Flags schema.
        /// </summary>
        public const string DisableFmSchemaCompatibility = "AZURE_APP_CONFIGURATION_FM_SCHEMA_COMPATIBILITY_DISABLED";

        /// <summary>
        /// Environment variable to disable request tracing.
        /// The value of this variable is a boolean string, e.g. "true" or "false".
        /// </summary>
        public const string RequestTracingDisabled = "AZURE_APP_CONFIGURATION_TRACING_DISABLED";
    }
}
