// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Defines well known label filters that are used within Azure App Configuration.
    /// </summary>
    public class LabelFilter
    {
        /// <summary>
        /// The filter that matches key-values with a null label.
        /// </summary>
        public const string Null = "\0";
    }
}
