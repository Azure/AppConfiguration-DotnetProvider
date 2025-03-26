// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Defines well known tag filter values that are used within Azure App Configuration.
    /// </summary>
    public class TagValue
    {
        /// <summary>
        /// Matches null tag values.
        /// </summary>
        public const string Null = "\0";
    }
}
