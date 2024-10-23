// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//

namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Provides extension methods for configuration sources.
    /// </summary>
    public static class ConfigurationSourceExtensions
    {
        /// <summary>
        /// Determines whether the specified configuration source is an Azure App Configuration source.
        /// </summary>
        /// <param name="source">The configuration source to check.</param>
        /// <returns><c>true</c> if the specified source is an Azure App Configuration source; otherwise, <c>false</c>.</returns>
        public static bool IsAzureAppConfigurationSource(this IConfigurationSource source)
        {
            return source is AzureAppConfigurationSource;
        }
    }
}
