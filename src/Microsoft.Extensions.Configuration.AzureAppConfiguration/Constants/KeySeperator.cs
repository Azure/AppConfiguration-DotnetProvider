// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
//
namespace Microsoft.Extensions.Configuration.AzureAppConfiguration
{
    /// <summary>
    /// Delimiter used to separate the keys of all key-values retrieved from Azure App Configuration.
    /// <see href="https://docs.microsoft.com/en-us/azure/azure-app-configuration/concept-key-value#keys"/>
    /// </summary>
    public static class KeySeparator
    {
        /// <summary>
        /// Period (.) delimiter.
        /// </summary>
        public const string Period = ".";

        /// <summary>
        /// Comma (,) delimiter.
        /// </summary>
        public const string Comma = ",";

        /// <summary>
        /// Colon (:) delimiter.
        /// </summary>
        internal const string Colon = ":";

        /// <summary>
        /// Semicolon (;) delimiter.
        /// </summary>
        public const string Semicolon = ";";

        /// <summary>
        /// Forward slash (/) delimiter.
        /// </summary>
        public const string ForwardSlash = "/";

        /// <summary>
        /// Dash (-) delimiter.
        /// </summary>
        public const string Dash = "-";

        /// <summary>
        /// Underscore (_) delimiter.
        /// </summary>
        public const string Underscore = "_";

        /// <summary>
        /// Double underscore (__) delimiter.
        /// </summary>
        public const string DoubleUnderscore = "__";
    }
}
