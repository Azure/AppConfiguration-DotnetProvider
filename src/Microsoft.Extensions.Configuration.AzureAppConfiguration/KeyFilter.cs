// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Defines well known key filters that are used within Azure App Configuration.
    /// </summary>
    public class KeyFilter
    {
        /// <summary>
        /// The filter that matches key-values with any keys.
        /// </summary>
        public const string Any = "*";
    }
}
